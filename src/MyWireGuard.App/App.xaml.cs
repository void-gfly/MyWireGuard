using System.IO;
using System.Windows;
using System.Windows.Threading;
using MyWireGuard.App.Dialogs;
using MyWireGuard.App.Services;
using MyWireGuard.App.ViewModels;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Interop;
using MyWireGuard.Infrastructure.Logging;
using MyWireGuard.Infrastructure.Runtime;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.App;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? singleInstanceCoordinator;
    private IInterconnectService? interconnectService;
    private bool pendingShowMainWindowRequest;
    private bool unhandledExceptionHandlersRegistered;
    private int isShowingUnhandledExceptionDialog;

    protected override void OnStartup(StartupEventArgs e)
    {
        RegisterUnhandledExceptionHandlers();

        try
        {
            base.OnStartup(e);
            StartApplication(e);
        }
        catch (Exception exception)
        {
            HandleUnhandledException(exception, "OnStartup", shouldShutdown: true);
        }
    }

    private void StartApplication(StartupEventArgs e)
    {
        if (TryRunTunnelServiceHost(e.Args))
        {
            return;
        }

        singleInstanceCoordinator = new SingleInstanceCoordinator();
        var startupMode = AppStartupDecider.DetermineStartupMode(
            e.Args,
            singleInstanceCoordinator.TryAcquirePrimaryInstance());

        if (startupMode == AppStartupMode.NotifyExistingGui)
        {
            try
            {
                singleInstanceCoordinator.NotifyPrimaryInstanceToShowWindowAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to notify the primary GUI instance: {exception}");
            }

            Shutdown(0);
            return;
        }

        singleInstanceCoordinator.StartListening(HandleShowMainWindowRequestedAsync);

        var runtimePaths = new AppRuntimePaths();
        runtimePaths.EnsureCreated();
        var runtimeAssetLocator = new RuntimeAssetLocator(runtimePaths);
        runtimeAssetLocator.EnsureRuntimeAvailable();

        var logService = new InMemoryLogService();
        var configStore = new FileConfigStore(runtimePaths, new WgQuickParser());
        var neighborMetadataStore = new FileNeighborMetadataStore(runtimePaths);
        var interconnectRecordStore = new FileInterconnectRecordStore(runtimePaths, logService);
        var subnetCalculator = new IPv4SubnetCalculator();
        var privilegeService = new PrivilegeService();
        var keypairService = new TunnelDllKeypairService(runtimeAssetLocator);
        var tunnelServiceManager = new TunnelServiceManager(logService, runtimeAssetLocator);
        var systemInteractionService = new SystemInteractionService();
        var textInputDialogService = new TextInputDialogService();
        var messageService = new MessageService();
        interconnectService = new InterconnectService(
            logService,
            Path.Combine(AppContext.BaseDirectory, "Data", "RecvFile"),
            interconnectRecordStore,
            InterconnectLimits.DefaultPort);
        try
        {
            new InterconnectFirewallRuleManager()
                .EnsureInboundRuleAsync(InterconnectLimits.DefaultPort, Environment.ProcessPath ?? string.Empty, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            logService.WriteInfo($"Interconnect firewall inbound rule ensured for TCP {InterconnectLimits.DefaultPort}.");
        }
        catch (Exception exception)
        {
            logService.WriteError($"Failed to ensure interconnect firewall inbound rule: {exception.Message}");
        }
        interconnectService.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        var tunnelNeighbors = new TunnelNeighborsViewModel(
            neighborMetadataStore,
            new NetworkNeighborScanner(subnetCalculator, logService),
            subnetCalculator,
            logService,
            messageService,
            systemInteractionService,
            textInputDialogService,
            interconnectService,
            new FileDialogService());
        var fileDialogService = new FileDialogService();

        var mainWindowViewModel = new MainWindowViewModel(
            configStore,
            tunnelServiceManager,
            keypairService,
            logService,
            privilegeService,
            tunnelNeighbors,
            fileDialogService,
            messageService,
            interconnectService,
            systemInteractionService);

        var mainWindow = new MainWindow(mainWindowViewModel);
        MainWindow = mainWindow;
        mainWindow.Show();
        BringMainWindowToFrontIfPending(mainWindow);
    }

    private void RegisterUnhandledExceptionHandlers()
    {
        if (unhandledExceptionHandlersRegistered)
        {
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        unhandledExceptionHandlersRegistered = true;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "DispatcherUnhandledException", shouldShutdown: false);
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException($"Unhandled non-exception object: {e.ExceptionObject}");
        HandleUnhandledException(exception, "AppDomain.UnhandledException", shouldShutdown: false);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleUnhandledException(e.Exception, "TaskScheduler.UnobservedTaskException", shouldShutdown: false);
        e.SetObserved();
    }

    private void HandleUnhandledException(Exception exception, string source, bool shouldShutdown)
    {
        var report = BuildAndWriteCrashReport(exception, source);
        ShowUnhandledExceptionDialog(report);

        if (shouldShutdown)
        {
            Shutdown(1);
        }
    }

    private static string BuildAndWriteCrashReport(Exception exception, string source)
    {
        var occurredAt = DateTimeOffset.Now;
        var report = CrashDiagnostics.FormatReport(exception, source, occurredAt);

        try
        {
            var logPath = CrashDiagnostics.WriteCrashLog(GetDefaultAppDataDirectory(), report, occurredAt);
            return $"{report}{Environment.NewLine}CrashLogPath: {logPath}{Environment.NewLine}";
        }
        catch (Exception logException)
        {
            return string.Concat(
                report,
                Environment.NewLine,
                "CrashLogWriteError: ",
                logException,
                Environment.NewLine);
        }
    }

    private void ShowUnhandledExceptionDialog(string report)
    {
        if (Interlocked.Exchange(ref isShowingUnhandledExceptionDialog, 1) == 1)
        {
            return;
        }

        try
        {
            if (Dispatcher.CheckAccess())
            {
                ShowUnhandledExceptionDialogOnDispatcher(report);
                return;
            }

            Dispatcher.Invoke(() => ShowUnhandledExceptionDialogOnDispatcher(report));
        }
        catch (Exception exception)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show unhandled exception dialog: {exception}");
        }
        finally
        {
            Interlocked.Exchange(ref isShowingUnhandledExceptionDialog, 0);
        }
    }

    private void ShowUnhandledExceptionDialogOnDispatcher(string report)
    {
        var dialog = new UnhandledExceptionDialog(report);
        if (MainWindow is not null && MainWindow.IsVisible)
        {
            dialog.Owner = MainWindow;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        dialog.ShowDialog();
    }

    private static string GetDefaultAppDataDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MyWireGuard");
    }

    private bool TryRunTunnelServiceHost(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "/service", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var success = TunnelDllNative.RunTunnelService(args[1]);
        Shutdown(success ? 0 : 1);
        return true;
    }

    private Task HandleShowMainWindowRequestedAsync()
    {
        return Dispatcher.InvokeAsync(() =>
        {
            if (MainWindow is MainWindow mainWindow)
            {
                mainWindow.BringToFrontFromExternalActivation();
                return;
            }

            pendingShowMainWindowRequest = true;
        }).Task;
    }

    private void BringMainWindowToFrontIfPending(MainWindow mainWindow)
    {
        if (!pendingShowMainWindowRequest)
        {
            return;
        }

        pendingShowMainWindowRequest = false;
        mainWindow.BringToFrontFromExternalActivation();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        interconnectService?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        interconnectService = null;
        singleInstanceCoordinator?.Dispose();
        singleInstanceCoordinator = null;
        base.OnExit(e);
    }
}
