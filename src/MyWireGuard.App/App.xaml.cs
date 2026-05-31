using System.Windows;
using MyWireGuard.App.Services;
using System.IO;
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

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

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
