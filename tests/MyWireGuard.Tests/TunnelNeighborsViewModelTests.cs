using System.Windows;
using System.Windows.Threading;
using MyWireGuard.App.Services;
using MyWireGuard.App.ViewModels;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Tests;

public sealed class TunnelNeighborsViewModelTests
{
    private static readonly Lazy<Dispatcher> StaDispatcher = new(() => WpfTestHost.Dispatcher);

    [Fact]
    public void LoadTunnelAsync_ShouldNotAutoScanWhenMetadataHasCompletedScan()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var metadataStore = new InMemoryNeighborMetadataStore();
            metadataStore.Seed(new NeighborMetadata
            {
                TunnelName = "first",
                SubnetCidr = "10.10.0.0/30",
                LastScanStartedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
                LastScanCompletedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            }, new NeighborHost
            {
                IpAddress = "10.10.0.1",
                IsAlive = true,
                PingMs = 1,
                LastScannedAt = DateTimeOffset.UtcNow.AddMinutes(-1)
            });

            var scanner = new ControlledScanner();
            var viewModel = new TunnelNeighborsViewModel(
                metadataStore,
                scanner,
                new FixedSubnetCalculator(),
                new TestLogService(),
                new TestMessageService(),
                new TestSystemInteractionService(),
                new TestTextInputDialogService(),
                new TestInterconnectService(),
                new TestFileDialogService());

            try
            {
                await viewModel.LoadTunnelAsync(CreateProfile("first", "10.10.0.2/30"), isConnected: true);
                await PumpDispatcherAsync();

                Assert.Equal(0, scanner.ScanCallCount);
                Assert.Contains(viewModel.Hosts, host => host.IpAddress == "10.10.0.1");
            }
            finally
            {
                scanner.CompletePendingScans();
                viewModel.Dispose();
            }
        });
    }

    [Fact]
    public void LoadTunnelAsync_ShouldIgnoreCanceledScanUpdatesFromPreviousTunnel()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();

            var scanner = new ControlledScanner();
            var viewModel = new TunnelNeighborsViewModel(
                new InMemoryNeighborMetadataStore(),
                scanner,
                new FixedSubnetCalculator(),
                new TestLogService(),
                new TestMessageService(),
                new TestSystemInteractionService(),
                new TestTextInputDialogService(),
                new TestInterconnectService(),
                new TestFileDialogService());

            var firstTunnel = CreateProfile("first", "10.10.0.2/30");
            var secondTunnel = CreateProfile("second", "10.20.0.2/30");

            try
            {
                await viewModel.LoadTunnelAsync(firstTunnel, isConnected: true);
                await WaitUntilAsync(() => scanner.FirstScanStarted.Task.IsCompleted);

                await viewModel.LoadTunnelAsync(secondTunnel, isConnected: true);

                Assert.True(scanner.FirstScanCancellationToken.IsCancellationRequested);

                scanner.ReportFirstScanHost(new NeighborHost
                {
                    IpAddress = "10.10.0.1",
                    IsAlive = true,
                    PingMs = 1
                });
                await PumpDispatcherAsync();

                Assert.DoesNotContain(viewModel.Hosts, host => host.IpAddress == "10.10.0.1");
            }
            finally
            {
                scanner.CompletePendingScans();
                viewModel.Dispose();
            }
        });
    }

    private static TunnelProfile CreateProfile(string name, string address)
    {
        var profile = new TunnelProfile { Name = name };
        profile.Interface.Addresses.Add(address);
        return profile;
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (condition())
            {
                return;
            }

            await PumpDispatcherAsync();
            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not reached.");
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

    private sealed class ControlledScanner : INetworkNeighborScanner
    {
        private IProgress<NeighborHostUpdate>? firstHostProgress;
        private readonly TaskCompletionSource<NeighborScanResult> pendingScan = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int scanCallCount;

        public TaskCompletionSource FirstScanStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationToken FirstScanCancellationToken { get; private set; }

        public int ScanCallCount => scanCallCount;

        public Task<NeighborScanResult> ScanAsync(
            TunnelProfile profile,
            NeighborMetadata existingMetadata,
            IProgress<NeighborScanProgress>? progress = null,
            IProgress<NeighborHostUpdate>? hostUpdateProgress = null,
            NeighborScanOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref scanCallCount);

            if (profile.Name == "first")
            {
                firstHostProgress = hostUpdateProgress;
                FirstScanCancellationToken = cancellationToken;
                FirstScanStarted.TrySetResult();
                return pendingScan.Task;
            }

            return pendingScan.Task;
        }

        public void ReportFirstScanHost(NeighborHost host)
        {
            firstHostProgress?.Report(new NeighborHostUpdate
            {
                Phase = NeighborScanPhase.Ping,
                Host = host
            });
        }

        public void CompletePendingScans()
        {
            pendingScan.TrySetResult(new NeighborScanResult
            {
                TunnelName = "pending",
                SubnetCidr = "10.20.0.0/30",
                StartedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private sealed class InMemoryNeighborMetadataStore : INeighborMetadataStore
    {
        private readonly Dictionary<string, NeighborMetadata> metadata = new(StringComparer.OrdinalIgnoreCase);

        public void Seed(NeighborMetadata value, params NeighborHost[] hosts)
        {
            foreach (var host in hosts)
            {
                value.Hosts.Add(host);
            }

            metadata[value.TunnelName] = value;
        }

        public Task<NeighborMetadata> GetAsync(string tunnelName, CancellationToken cancellationToken = default)
        {
            if (!metadata.TryGetValue(tunnelName, out var value))
            {
                value = new NeighborMetadata { TunnelName = tunnelName };
                metadata[tunnelName] = value;
            }

            return Task.FromResult(value);
        }

        public string GetPath(string tunnelName)
        {
            return $"{tunnelName}.neighbors.json";
        }

        public Task SaveAsync(NeighborMetadata value, CancellationToken cancellationToken = default)
        {
            metadata[value.TunnelName] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSubnetCalculator : IIPv4SubnetCalculator
    {
        public bool TryGetPrimarySubnet(TunnelProfile profile, out string subnetCidr)
        {
            subnetCidr = profile.Name == "first" ? "10.10.0.0/30" : "10.20.0.0/30";
            return true;
        }

        public IReadOnlyList<string> EnumerateHostAddresses(string subnetCidr)
        {
            return subnetCidr.StartsWith("10.10.", StringComparison.Ordinal)
                ? ["10.10.0.1", "10.10.0.2"]
                : ["10.20.0.1", "10.20.0.2"];
        }
    }

    private sealed class TestLogService : ILogService
    {
        public event EventHandler<LogEntry>? EntryWritten
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LogEntry> GetEntries()
        {
            return [];
        }

        public void WriteInfo(string message)
        {
        }

        public void WriteError(string message)
        {
        }
    }

    private sealed class TestMessageService : IMessageService
    {
        public void ShowInfo(string message, string title)
        {
        }

        public void ShowError(string message, string title)
        {
        }

        public bool Confirm(string message, string title)
        {
            return true;
        }

        public ExitConfirmationResult ConfirmExitWithActiveTunnels(string message, string title)
        {
            return ExitConfirmationResult.Cancel;
        }
    }

    private sealed class TestSystemInteractionService : ISystemInteractionService
    {
        public void CopyText(string text)
        {
        }

        public void CopyFile(string path)
        {
        }

        public void OpenRemoteDesktop(string ipAddress)
        {
        }

        public void OpenSsh(string ipAddress)
        {
        }

        public void OpenFile(string path)
        {
        }

        public void OpenContainingFolder(string path)
        {
        }

        public void OpenFolder(string path)
        {
        }
    }

    private sealed class TestTextInputDialogService : ITextInputDialogService
    {
        public bool TryShow(string title, string prompt, string initialValue, out string value)
        {
            value = initialValue;
            return true;
        }
    }

    private sealed class TestInterconnectService : IInterconnectService
    {
        public string ListenerStatusText => "已停止";
        public int ListenerPort => 0;
        public event EventHandler? ListenerStateChanged
        {
            add { }
            remove { }
        }
        public event EventHandler<InterconnectReceiveTextRecord>? TextReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<InterconnectReceiveFileRecord>? FileReceived
        {
            add { }
            remove { }
        }

        public event EventHandler<InterconnectSendProgress>? SendProgressChanged
        {
            add { }
            remove { }
        }
        public IReadOnlyList<InterconnectReceiveTextRecord> GetReceivedTextRecords() => [];
        public IReadOnlyList<InterconnectReceiveFileRecord> GetReceivedFileRecords() => [];
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendTextAsync(string ipAddress, int port, string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SendFileAsync(string ipAddress, int port, string filePath, IProgress<InterconnectSendProgress>? progress, CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TestFileDialogService : IFileDialogService
    {
        public string? PickImportPath() => null;
        public string? PickExportPath(string tunnelName) => null;
        public string? PickSendFilePath() => null;
    }
}
