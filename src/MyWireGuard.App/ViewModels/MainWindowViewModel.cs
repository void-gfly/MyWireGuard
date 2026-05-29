using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using MyWireGuard.App.Services;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IConfigStore configStore;
    private readonly ITunnelServiceManager tunnelServiceManager;
    private readonly IKeypairService keypairService;
    private readonly ILogService logService;
    private readonly IPrivilegeService privilegeService;
    private readonly IFileDialogService fileDialogService;
    private readonly IMessageService messageService;
    private readonly TunnelNeighborsViewModel tunnelNeighbors;
    private readonly DispatcherTimer statusRefreshTimer;

    private TunnelItemViewModel? selectedTunnel;
    private string editableConfigText = string.Empty;
    private string runtimeStatus = string.Empty;
    private bool isInitialized;
    private bool isRefreshingServiceStatuses;
    private int selectedTabIndex;
    private string generatedPublicKey = string.Empty;
    private string generatedPrivateKey = string.Empty;

    public MainWindowViewModel(
        IConfigStore configStore,
        ITunnelServiceManager tunnelServiceManager,
        IKeypairService keypairService,
        ILogService logService,
        IPrivilegeService privilegeService,
        TunnelNeighborsViewModel tunnelNeighbors,
        IFileDialogService fileDialogService,
        IMessageService messageService)
    {
        this.configStore = configStore;
        this.tunnelServiceManager = tunnelServiceManager;
        this.keypairService = keypairService;
        this.logService = logService;
        this.privilegeService = privilegeService;
        this.tunnelNeighbors = tunnelNeighbors;
        this.fileDialogService = fileDialogService;
        this.messageService = messageService;
        statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        statusRefreshTimer.Tick += OnStatusRefreshTimerTick;

        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        ImportCommand = new AsyncRelayCommand(ImportAsync);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => SelectedTunnel is not null);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => SelectedTunnel is not null);
        StartCommand = new AsyncRelayCommand(StartAsync, () => SelectedTunnel is not null);
        StopCommand = new AsyncRelayCommand(StopAsync, () => SelectedTunnel is not null);
        ToggleTunnelCommand = new AsyncRelayCommand(ToggleTunnelAsync, CanToggleTunnel);
        RemoveCommand = new AsyncRelayCommand(RemoveAsync, () => SelectedTunnel is not null);
        GenerateKeypairCommand = new AsyncRelayCommand(GenerateKeypairAsync);
        EditConfigCommand = new AsyncRelayCommand(() => { SelectedTabIndex = 1; return Task.CompletedTask; }, () => SelectedTunnel is not null);
        ClearLogCommand = new AsyncRelayCommand(() => { Application.Current.Dispatcher.Invoke(LogEntries.Clear); return Task.CompletedTask; });
        CopyPublicKeyCommand = new AsyncRelayCommand(
            () => { Clipboard.SetText(GeneratedPublicKey); return Task.CompletedTask; },
            () => !string.IsNullOrEmpty(GeneratedPublicKey));
        CopyPrivateKeyCommand = new AsyncRelayCommand(
            () => { Clipboard.SetText(GeneratedPrivateKey); return Task.CompletedTask; },
            () => !string.IsNullOrEmpty(GeneratedPrivateKey));

        runtimeStatus = tunnelServiceManager.IsRuntimeAvailable()
            ? "运行时已就绪"
            : "应用目录中缺少 tunnel.dll";

        PrivilegeStatus = privilegeService.IsElevated
            ? "已使用管理员权限运行"
            : "当前未以管理员权限运行";

        foreach (var entry in logService.GetEntries())
        {
            LogEntries.Add(entry);
        }

        logService.EntryWritten += OnEntryWritten;
    }

    public ObservableCollection<TunnelItemViewModel> Tunnels { get; } = [];

    public ObservableCollection<LogEntry> LogEntries { get; } = [];

    public TunnelNeighborsViewModel TunnelNeighbors => tunnelNeighbors;

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand ImportCommand { get; }

    public AsyncRelayCommand SaveCommand { get; }

    public AsyncRelayCommand ExportCommand { get; }

    public AsyncRelayCommand StartCommand { get; }

    public AsyncRelayCommand StopCommand { get; }

    public AsyncRelayCommand ToggleTunnelCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public AsyncRelayCommand GenerateKeypairCommand { get; }

    public AsyncRelayCommand EditConfigCommand { get; }

    public AsyncRelayCommand ClearLogCommand { get; }

    public AsyncRelayCommand CopyPublicKeyCommand { get; }

    public AsyncRelayCommand CopyPrivateKeyCommand { get; }

    public string RuntimeStatus
    {
        get => runtimeStatus;
        private set
        {
            if (SetProperty(ref runtimeStatus, value))
            {
                RaisePropertyChanged(nameof(TrayStatusText));
            }
        }
    }

    public string PrivilegeStatus { get; }

    public string TrayStatusText
    {
        get
        {
            if (!string.Equals(RuntimeStatus, "运行时已就绪", StringComparison.Ordinal))
            {
                return RuntimeStatus;
            }

            if (SelectedTunnel is null)
            {
                return "未选择隧道";
            }

            return $"{SelectedTunnel.Name}: {SelectedTunnel.StatusDisplay}";
        }
    }

    public int SelectedTabIndex
    {
        get => selectedTabIndex;
        set => SetProperty(ref selectedTabIndex, value);
    }

    public string GeneratedPublicKey
    {
        get => generatedPublicKey;
        private set => SetProperty(ref generatedPublicKey, value);
    }

    public string GeneratedPrivateKey
    {
        get => generatedPrivateKey;
        private set => SetProperty(ref generatedPrivateKey, value);
    }

    public string ToggleTunnelButtonContent => SelectedTunnel?.Status switch
    {
        TunnelStatus.Started or TunnelStatus.Starting => "■  停止",
        _ => "▶  启动"
    };

    public TunnelItemViewModel? SelectedTunnel
    {
        get => selectedTunnel;
        set
        {
            if (ReferenceEquals(selectedTunnel, value))
            {
                return;
            }

            if (selectedTunnel is not null)
            {
                selectedTunnel.PropertyChanged -= OnSelectedTunnelPropertyChanged;
            }

            if (SetProperty(ref selectedTunnel, value))
            {
                if (value is not null)
                {
                    value.PropertyChanged += OnSelectedTunnelPropertyChanged;
                }

                EditableConfigText = value?.Profile.ConfigText ?? string.Empty;
                _ = tunnelNeighbors.LoadTunnelAsync(value?.Profile, value?.Status == TunnelStatus.Started);
                RaisePropertyChanged(nameof(ToggleTunnelButtonContent));
                RaisePropertyChanged(nameof(TrayStatusText));
                NotifyCommandStateChanged();
            }
        }
    }

    public string EditableConfigText
    {
        get => editableConfigText;
        set => SetProperty(ref editableConfigText, value);
    }

    public async Task InitializeAsync()
    {
        if (isInitialized)
        {
            return;
        }

        isInitialized = true;
        logService.WriteInfo("MyWireGuard 界面已启动。");
        await RefreshAsync().ConfigureAwait(false);
    }

    public async Task RefreshAsync()
    {
        var selectedName = SelectedTunnel?.Name;
        var profiles = await configStore.GetAllAsync().ConfigureAwait(false);
        var items = new List<TunnelItemViewModel>(profiles.Count);

        foreach (var profile in profiles)
        {
            await tunnelServiceManager.EnsureServiceConfigurationAsync(profile).ConfigureAwait(false);
            var status = await tunnelServiceManager.GetStatusAsync(profile.Name).ConfigureAwait(false);
            items.Add(new TunnelItemViewModel(profile, status));
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Tunnels.Clear();
            foreach (var item in items)
            {
                Tunnels.Add(item);
            }

            SelectedTunnel = Tunnels.FirstOrDefault(tunnel => string.Equals(tunnel.Name, selectedName, StringComparison.OrdinalIgnoreCase))
                ?? Tunnels.FirstOrDefault();
        });

        UpdateStatusRefreshState();

        logService.WriteInfo($"Loaded {items.Count} tunnel profile(s).");
    }

    private async Task ImportAsync()
    {
        var sourcePath = fileDialogService.PickImportPath();
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        try
        {
            var profile = await configStore.ImportAsync(sourcePath).ConfigureAwait(false);
            logService.WriteInfo($"Imported tunnel '{profile.Name}' from '{sourcePath}'.");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception, "导入失败");
        }
    }

    private async Task SaveAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        try
        {
            var profile = new TunnelProfile
            {
                Name = SelectedTunnel.Name,
                ConfigPath = SelectedTunnel.Profile.ConfigPath,
                ConfigText = EditableConfigText
            };

            var savedProfile = await configStore.SaveAsync(profile).ConfigureAwait(false);
            logService.WriteInfo($"Saved tunnel '{savedProfile.Name}'.");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception, "保存失败");
        }
    }

    private async Task ExportAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        var destinationPath = fileDialogService.PickExportPath(SelectedTunnel.Name);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return;
        }

        try
        {
            await configStore.ExportAsync(SelectedTunnel.Name, destinationPath).ConfigureAwait(false);
            logService.WriteInfo($"Exported tunnel '{SelectedTunnel.Name}' to '{destinationPath}'.");
        }
        catch (Exception exception)
        {
            HandleException(exception, "导出失败");
        }
    }

    private async Task StartAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        try
        {
            await SaveAsync().ConfigureAwait(false);
            var profile = await configStore.GetAsync(SelectedTunnel.Name).ConfigureAwait(false)
                ?? throw new InvalidOperationException("保存后无法重新加载隧道配置。");

            await tunnelServiceManager.StartAsync(profile).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception, "Start failed");
        }
    }

    private async Task StopAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        try
        {
            await tunnelServiceManager.StopAsync(SelectedTunnel.Name).ConfigureAwait(false);
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception, "Stop failed");
        }
    }

    private bool CanToggleTunnel()
    {
        return SelectedTunnel?.Status is TunnelStatus.Started or TunnelStatus.Stopped;
    }

    private Task ToggleTunnelAsync()
    {
        return SelectedTunnel?.Status == TunnelStatus.Started
            ? StopAsync()
            : StartAsync();
    }

    private async void OnStatusRefreshTimerTick(object? sender, EventArgs e)
    {
        if (isRefreshingServiceStatuses)
        {
            return;
        }

        try
        {
            isRefreshingServiceStatuses = true;
            await RefreshTrackedTunnelStatusesAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            statusRefreshTimer.Stop();
            HandleException(exception, "状态刷新失败");
        }
        finally
        {
            isRefreshingServiceStatuses = false;
        }
    }

    private async Task RefreshTrackedTunnelStatusesAsync()
    {
        var trackedTunnels = await Application.Current.Dispatcher.InvokeAsync(() =>
            Tunnels.Where(tunnel => tunnel.Status.RequiresServiceStatusRefresh())
                .ToList());

        if (trackedTunnels.Count == 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(statusRefreshTimer.Stop);
            return;
        }

        foreach (var tunnel in trackedTunnels)
        {
            var latestStatus = await tunnelServiceManager.GetStatusAsync(tunnel.Name).ConfigureAwait(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (latestStatus.IsUnexpectedStopFrom(tunnel.Status))
                {
                    logService.WriteError($"Tunnel '{tunnel.Name}' service stopped unexpectedly. Windows service recovery will try to restart it; check the Service Control Manager event log for the exit code.");
                }

                tunnel.Status = latestStatus;
            });
        }

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            RaisePropertyChanged(nameof(ToggleTunnelButtonContent));
            RaisePropertyChanged(nameof(TrayStatusText));
            NotifyCommandStateChanged();
            UpdateStatusRefreshState();
        });
    }

    private void OnSelectedTunnelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TunnelItemViewModel.Status))
        {
            _ = tunnelNeighbors.SetConnectionStateAsync(SelectedTunnel?.Status == TunnelStatus.Started);
            RaisePropertyChanged(nameof(ToggleTunnelButtonContent));
            RaisePropertyChanged(nameof(TrayStatusText));
            NotifyCommandStateChanged();
        }
    }

    private void UpdateStatusRefreshState()
    {
        var hasTrackedTunnel = Tunnels.Any(tunnel => tunnel.Status.RequiresServiceStatusRefresh());
        if (hasTrackedTunnel)
        {
            if (!statusRefreshTimer.IsEnabled)
            {
                statusRefreshTimer.Start();
            }

            return;
        }

        statusRefreshTimer.Stop();
    }

    private async Task RemoveAsync()
    {
        if (SelectedTunnel is null)
        {
            return;
        }

        if (!messageService.Confirm($"确定要删除隧道 \"{SelectedTunnel.Name}\" 吗？此操作不可撤销。", "确认删除"))
        {
            return;
        }

        try
        {
            await tunnelServiceManager.RemoveAsync(SelectedTunnel.Name).ConfigureAwait(false);
            await configStore.DeleteAsync(SelectedTunnel.Name).ConfigureAwait(false);
            logService.WriteInfo($"Deleted tunnel '{SelectedTunnel.Name}'.");
            await RefreshAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            HandleException(exception, "删除失败");
        }
    }

    private Task GenerateKeypairAsync()
    {
        try
        {
            var keypair = keypairService.Generate();
            GeneratedPublicKey = keypair.PublicKey;
            GeneratedPrivateKey = keypair.PrivateKey;
            logService.WriteInfo("Generated a WireGuard keypair.");
            NotifyCommandStateChanged();
        }
        catch (Exception exception)
        {
            HandleException(exception, "Key generation failed");
        }

        return Task.CompletedTask;
    }

    private void HandleException(Exception exception, string title)
    {
        logService.WriteError(exception.Message);
        messageService.ShowError(exception.Message, title);
        RuntimeStatus = tunnelServiceManager.IsRuntimeAvailable()
            ? "运行时已就绪"
            : "应用目录中缺少 tunnel.dll";
    }

    private void OnEntryWritten(object? sender, LogEntry entry)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Add(entry);
            while (LogEntries.Count > 200)
            {
                LogEntries.RemoveAt(0);
            }
        });
    }

    private void NotifyCommandStateChanged()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
        SaveCommand.NotifyCanExecuteChanged();
        ExportCommand.NotifyCanExecuteChanged();
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        ToggleTunnelCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
        GenerateKeypairCommand.NotifyCanExecuteChanged();
        EditConfigCommand.NotifyCanExecuteChanged();
        CopyPublicKeyCommand.NotifyCanExecuteChanged();
        CopyPrivateKeyCommand.NotifyCanExecuteChanged();
    }

    public void Shutdown()
    {
        statusRefreshTimer.Stop();
        statusRefreshTimer.Tick -= OnStatusRefreshTimerTick;
        logService.EntryWritten -= OnEntryWritten;
        tunnelNeighbors.Dispose();
    }

    public async Task<ExitConfirmationResult> ConfirmExitAsync()
    {
        var profiles = await configStore.GetAllAsync().ConfigureAwait(false);
        var activeTunnelNames = new List<string>();

        foreach (var profile in profiles)
        {
            var status = await tunnelServiceManager.GetStatusAsync(profile.Name).ConfigureAwait(false);
            if (status is TunnelStatus.Started or TunnelStatus.Starting or TunnelStatus.Stopping)
            {
                activeTunnelNames.Add(profile.Name);
            }
        }

        if (activeTunnelNames.Count == 0)
        {
            return ExitConfirmationResult.StopTunnelsAndExit;
        }

        var message = "检测到有活动隧道。\n\n"
            + $"活动隧道: {string.Join(", ", activeTunnelNames)}\n\n"
            + "选择“是”: 保持活动隧道，只退出 UI 界面。\n"
            + "选择“否”: 停止所有活动隧道并退出程序。\n"
            + "选择“取消”: 返回程序。";

        return await Application.Current.Dispatcher.InvokeAsync(() => messageService.ConfirmExitWithActiveTunnels(message, "确认退出"));
    }

    public async Task StopActiveTunnelsAsync()
    {
        var profiles = await configStore.GetAllAsync().ConfigureAwait(false);

        foreach (var profile in profiles)
        {
            var status = await tunnelServiceManager.GetStatusAsync(profile.Name).ConfigureAwait(false);
            if (status is TunnelStatus.Stopped or TunnelStatus.Unknown)
            {
                continue;
            }

            try
            {
                await tunnelServiceManager.StopAsync(profile.Name).ConfigureAwait(false);
                logService.WriteInfo($"Stopped tunnel '{profile.Name}' during application shutdown.");
            }
            catch (Exception exception)
            {
                logService.WriteError($"Failed to stop tunnel '{profile.Name}' during shutdown: {exception.Message}");
            }
        }
    }
}
