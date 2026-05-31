using System.Windows;
using System.Windows.Threading;
using MyWireGuard.App.Services;
using MyWireGuard.App.ViewModels;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Tests;

public sealed class MainWindowViewModelInterconnectTests
{
    private static readonly Lazy<Dispatcher> StaDispatcher = new(() => WpfTestHost.Dispatcher);

    [Fact]
    public void InterconnectEvents_ShouldPopulateReceiveCollections()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var interconnectService = new FakeInterconnectService();
            var viewModel = new MainWindowViewModel(
                new FakeConfigStore(),
                new FakeTunnelServiceManager(),
                new FakeKeypairService(),
                new TestLogService(),
                new FakePrivilegeService(),
                new TunnelNeighborsViewModel(
                    new FakeNeighborMetadataStore(),
                    new FakeScanner(),
                    new FakeSubnetCalculator(),
                    new TestLogService(),
                    new TestMessageService(),
                    new TestSystemInteractionService(),
                    new TestTextInputDialogService(),
                    interconnectService,
                    new FakeFileDialogService()),
                new FakeFileDialogService(),
                new TestMessageService(),
                interconnectService,
                new TestSystemInteractionService());

            try
            {
                interconnectService.RaiseText(new InterconnectReceiveTextRecord(DateTimeOffset.Now, "10.0.0.2", "hello"));
                interconnectService.RaiseFile(new InterconnectReceiveFileRecord(DateTimeOffset.Now, "10.0.0.3", "demo.txt", 7, @"C:\Temp\demo.txt"));
                await PumpDispatcherAsync();

                Assert.Single(viewModel.ReceivedTexts);
                Assert.Single(viewModel.ReceivedFiles);
                Assert.Equal("hello", viewModel.ReceivedTexts[0].Text);
                Assert.Equal(@"C:\Temp\demo.txt", viewModel.ReceivedFiles[0].SavedPath);
            }
            finally
            {
                viewModel.Shutdown();
                await interconnectService.DisposeAsync();
            }
        });
    }

    [Fact]
    public void InterconnectListenerState_ShouldFlowIntoMainWindowViewModel()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var interconnectService = new FakeInterconnectService
            {
                ListenerStatusText = "监听中",
                ListenerPort = 7727
            };
            var viewModel = new MainWindowViewModel(
                new FakeConfigStore(),
                new FakeTunnelServiceManager(),
                new FakeKeypairService(),
                new TestLogService(),
                new FakePrivilegeService(),
                new TunnelNeighborsViewModel(
                    new FakeNeighborMetadataStore(),
                    new FakeScanner(),
                    new FakeSubnetCalculator(),
                    new TestLogService(),
                    new TestMessageService(),
                    new TestSystemInteractionService(),
                    new TestTextInputDialogService(),
                    interconnectService,
                    new FakeFileDialogService()),
                new FakeFileDialogService(),
                new TestMessageService(),
                interconnectService,
                new TestSystemInteractionService());

            try
            {
                Assert.Equal("监听中", viewModel.InterconnectListenerStatus);
                Assert.Equal("7727", viewModel.InterconnectListenerPort);

                interconnectService.UpdateListenerState("已停止", 0);
                await PumpDispatcherAsync();

                Assert.Equal("已停止", viewModel.InterconnectListenerStatus);
                Assert.Equal("-", viewModel.InterconnectListenerPort);
            }
            finally
            {
                viewModel.Shutdown();
                await interconnectService.DisposeAsync();
            }
        });
    }

    [Fact]
    public void Constructor_ShouldLoadPreexistingInterconnectHistory()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var interconnectService = new FakeInterconnectService();
            interconnectService.SeedTextRecords(
            [
                new InterconnectReceiveTextRecord(DateTimeOffset.Now, "10.0.0.9", "latest"),
                new InterconnectReceiveTextRecord(DateTimeOffset.Now.AddMinutes(-1), "10.0.0.8", "older")
            ]);
            interconnectService.SeedFileRecords(
            [
                new InterconnectReceiveFileRecord(DateTimeOffset.Now, "10.0.0.7", "latest.txt", 9, @"C:\Temp\latest.txt"),
                new InterconnectReceiveFileRecord(DateTimeOffset.Now.AddMinutes(-1), "10.0.0.6", "older.txt", 8, @"C:\Temp\older.txt")
            ]);

            var viewModel = new MainWindowViewModel(
                new FakeConfigStore(),
                new FakeTunnelServiceManager(),
                new FakeKeypairService(),
                new TestLogService(),
                new FakePrivilegeService(),
                new TunnelNeighborsViewModel(
                    new FakeNeighborMetadataStore(),
                    new FakeScanner(),
                    new FakeSubnetCalculator(),
                    new TestLogService(),
                    new TestMessageService(),
                    new TestSystemInteractionService(),
                    new TestTextInputDialogService(),
                    interconnectService,
                    new FakeFileDialogService()),
                new FakeFileDialogService(),
                new TestMessageService(),
                interconnectService,
                new TestSystemInteractionService());

            try
            {
                await PumpDispatcherAsync();

                Assert.Equal(2, viewModel.ReceivedTexts.Count);
                Assert.Equal("latest", viewModel.ReceivedTexts[0].Text);
                Assert.Equal("older", viewModel.ReceivedTexts[1].Text);
                Assert.Equal(2, viewModel.ReceivedFiles.Count);
                Assert.Equal("latest.txt", viewModel.ReceivedFiles[0].FileName);
                Assert.Equal("older.txt", viewModel.ReceivedFiles[1].FileName);
            }
            finally
            {
                viewModel.Shutdown();
                await interconnectService.DisposeAsync();
            }
        });
    }

    [Fact]
    public void ToggleTunnelCommand_ShouldRemoveService_WhenStartedTunnelIsStoppedFromUi()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var tunnelServiceManager = new FakeTunnelServiceManager();
            var viewModel = CreateViewModel(tunnelServiceManager, new FakeInterconnectService());
            viewModel.SelectedTunnel = new TunnelItemViewModel(CreateProfile("demo"), TunnelStatus.Started);

            try
            {
                viewModel.ToggleTunnelCommand.Execute(null);
                await WaitUntilAsync(() => tunnelServiceManager.RemovedTunnelNames.Contains("demo"));

                Assert.Empty(tunnelServiceManager.StoppedTunnelNames);
                Assert.Contains("demo", tunnelServiceManager.RemovedTunnelNames);
            }
            finally
            {
                viewModel.Shutdown();
            }
        });
    }

    [Fact]
    public void StopActiveTunnelsAsync_ShouldRemoveActiveServicesToPreventAutoRestart()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var tunnelServiceManager = new FakeTunnelServiceManager();
            tunnelServiceManager.SetStatus("started", TunnelStatus.Started);
            tunnelServiceManager.SetStatus("stopped", TunnelStatus.Stopped);
            var configStore = new FakeConfigStore();
            configStore.SeedProfiles(CreateProfile("started"), CreateProfile("stopped"));
            var viewModel = CreateViewModel(tunnelServiceManager, new FakeInterconnectService(), configStore);

            try
            {
                await viewModel.StopActiveTunnelsAsync();

                Assert.Equal(["started"], tunnelServiceManager.RemovedTunnelNames);
                Assert.Empty(tunnelServiceManager.StoppedTunnelNames);
            }
            finally
            {
                viewModel.Shutdown();
            }
        });
    }

    private static void EnsureApplication()
    {
        if (Application.Current is null)
        {
            throw new InvalidOperationException("The test WPF application was not initialized.");
        }
    }

    private static void RunOnStaThread(Func<Task> action)
    {
        Exception? exception = null;
        using var completed = new ManualResetEventSlim();
        StaDispatcher.Value.BeginInvoke(new Action(async () =>
        {
            try
            {
                await action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
            finally
            {
                completed.Set();
            }
        }));

        completed.Wait();

        if (exception is not null)
        {
            throw exception;
        }
    }

    private static Task PumpDispatcherAsync()
    {
        var frame = new DispatcherFrame();
        _ = Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        return Task.CompletedTask;
    }

    private static MainWindowViewModel CreateViewModel(
        FakeTunnelServiceManager tunnelServiceManager,
        FakeInterconnectService interconnectService,
        FakeConfigStore? configStore = null)
    {
        return new MainWindowViewModel(
            configStore ?? new FakeConfigStore(),
            tunnelServiceManager,
            new FakeKeypairService(),
            new TestLogService(),
            new FakePrivilegeService(),
            new TunnelNeighborsViewModel(
                new FakeNeighborMetadataStore(),
                new FakeScanner(),
                new FakeSubnetCalculator(),
                new TestLogService(),
                new TestMessageService(),
                new TestSystemInteractionService(),
                new TestTextInputDialogService(),
                interconnectService,
                new FakeFileDialogService()),
            new FakeFileDialogService(),
            new TestMessageService(),
            interconnectService,
            new TestSystemInteractionService());
    }

    private static TunnelProfile CreateProfile(string name)
    {
        return new TunnelProfile
        {
            Name = name,
            ConfigPath = $@"C:\Temp\{name}.conf"
        };
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await PumpDispatcherAsync();
            if (predicate())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Timed out waiting for view model operation.");
    }

    private sealed class FakeInterconnectService : IInterconnectService
    {
        private readonly List<InterconnectReceiveTextRecord> textRecords = [];
        private readonly List<InterconnectReceiveFileRecord> fileRecords = [];

        public string ListenerStatusText { get; set; } = "已停止";
        public int ListenerPort { get; set; }

        public event EventHandler? ListenerStateChanged;
        public event EventHandler<InterconnectReceiveTextRecord>? TextReceived;
        public event EventHandler<InterconnectReceiveFileRecord>? FileReceived;
        public event EventHandler<InterconnectSendProgress>? SendProgressChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<InterconnectReceiveTextRecord> GetReceivedTextRecords() => textRecords.ToArray();
        public IReadOnlyList<InterconnectReceiveFileRecord> GetReceivedFileRecords() => fileRecords.ToArray();
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTextAsync(string ipAddress, int port, string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendFileAsync(string ipAddress, int port, string filePath, IProgress<InterconnectSendProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public void RaiseText(InterconnectReceiveTextRecord record)
        {
            textRecords.Add(record);
            TextReceived?.Invoke(this, record);
        }

        public void RaiseFile(InterconnectReceiveFileRecord record)
        {
            fileRecords.Add(record);
            FileReceived?.Invoke(this, record);
        }

        public void SeedTextRecords(IEnumerable<InterconnectReceiveTextRecord> records)
        {
            textRecords.Clear();
            textRecords.AddRange(records);
        }

        public void SeedFileRecords(IEnumerable<InterconnectReceiveFileRecord> records)
        {
            fileRecords.Clear();
            fileRecords.AddRange(records);
        }

        public void UpdateListenerState(string statusText, int port)
        {
            ListenerStatusText = statusText;
            ListenerPort = port;
            ListenerStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed class FakeConfigStore : IConfigStore
    {
        private readonly List<TunnelProfile> profiles = [];

        public Task<IReadOnlyList<TunnelProfile>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TunnelProfile>>(profiles.ToArray());
        public Task<TunnelProfile?> GetAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<TunnelProfile?>(null);
        public Task<TunnelProfile> SaveAsync(TunnelProfile profile, CancellationToken cancellationToken = default) => Task.FromResult(profile);
        public Task<TunnelProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default) => Task.FromResult(new TunnelProfile());
        public Task ExportAsync(string name, string destinationPath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public string GetConfigPath(string name) => name;

        public void SeedProfiles(params TunnelProfile[] values)
        {
            profiles.Clear();
            profiles.AddRange(values);
        }
    }

    private sealed class FakeTunnelServiceManager : ITunnelServiceManager
    {
        private readonly Dictionary<string, TunnelStatus> statuses = new(StringComparer.OrdinalIgnoreCase);

        public List<string> StoppedTunnelNames { get; } = [];
        public List<string> RemovedTunnelNames { get; } = [];

        public Task<TunnelStatus> GetStatusAsync(string tunnelName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(statuses.TryGetValue(tunnelName, out var status) ? status : TunnelStatus.Stopped);
        }

        public Task EnsureServiceConfigurationAsync(TunnelProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartAsync(TunnelProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(string tunnelName, CancellationToken cancellationToken = default)
        {
            StoppedTunnelNames.Add(tunnelName);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string tunnelName, CancellationToken cancellationToken = default)
        {
            RemovedTunnelNames.Add(tunnelName);
            return Task.CompletedTask;
        }

        public string GetServiceName(string tunnelName) => tunnelName;
        public bool IsRuntimeAvailable() => true;

        public void SetStatus(string tunnelName, TunnelStatus status)
        {
            statuses[tunnelName] = status;
        }
    }

    private sealed class FakeKeypairService : IKeypairService
    {
        public GeneratedKeypair Generate() => new("public", "private");
    }

    private sealed class FakePrivilegeService : IPrivilegeService
    {
        public bool IsElevated => false;
    }

    private sealed class FakeNeighborMetadataStore : INeighborMetadataStore
    {
        public Task<NeighborMetadata> GetAsync(string tunnelName, CancellationToken cancellationToken = default) => Task.FromResult(new NeighborMetadata { TunnelName = tunnelName });
        public string GetPath(string tunnelName) => tunnelName;
        public Task SaveAsync(NeighborMetadata value, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeScanner : INetworkNeighborScanner
    {
        public Task<NeighborScanResult> ScanAsync(TunnelProfile profile, NeighborMetadata existingMetadata, IProgress<NeighborScanProgress>? progress = null, IProgress<NeighborHostUpdate>? hostUpdateProgress = null, NeighborScanOptions? options = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new NeighborScanResult { TunnelName = profile.Name, SubnetCidr = "10.0.0.0/24" });
    }

    private sealed class FakeSubnetCalculator : IIPv4SubnetCalculator
    {
        public bool TryGetPrimarySubnet(TunnelProfile profile, out string subnetCidr)
        {
            subnetCidr = "10.0.0.0/24";
            return true;
        }

        public IReadOnlyList<string> EnumerateHostAddresses(string subnetCidr) => ["10.0.0.1"];
    }

    private sealed class TestLogService : ILogService
    {
        public event EventHandler<LogEntry>? EntryWritten
        {
            add { }
            remove { }
        }
        public IReadOnlyList<LogEntry> GetEntries() => [];
        public void WriteInfo(string message) { }
        public void WriteError(string message) { }
    }

    private sealed class TestMessageService : IMessageService
    {
        public void ShowInfo(string message, string title) { }
        public void ShowError(string message, string title) { }
        public bool Confirm(string message, string title) => true;
        public ExitConfirmationResult ConfirmExitWithActiveTunnels(string message, string title) => ExitConfirmationResult.Cancel;
    }

    private sealed class TestSystemInteractionService : ISystemInteractionService
    {
        public void CopyText(string text) { }
        public string GetClipboardText() => string.Empty;
        public void CopyFile(string path) { }
        public void OpenRemoteDesktop(string ipAddress) { }
        public void OpenSsh(string ipAddress) { }
        public void OpenFile(string path) { }
        public void OpenContainingFolder(string path) { }
        public void OpenFolder(string path) { }
    }

    private sealed class TestTextInputDialogService : ITextInputDialogService
    {
        public bool TryShow(string title, string prompt, string initialValue, out string value)
        {
            value = initialValue;
            return true;
        }

        public bool TryShowMultiline(string title, string prompt, string initialValue, out string value)
        {
            value = initialValue;
            return true;
        }
    }

    private sealed class FakeFileDialogService : IFileDialogService
    {
        public string? PickImportPath() => null;
        public string? PickExportPath(string tunnelName) => null;
        public string? PickSendFilePath() => null;
    }
}
