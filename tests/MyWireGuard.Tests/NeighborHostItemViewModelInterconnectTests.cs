using System.Windows;
using System.Windows.Threading;
using MyWireGuard.App.Services;
using MyWireGuard.App.ViewModels;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Tests;

public sealed class NeighborHostItemViewModelInterconnectTests : IDisposable
{
    private static readonly Lazy<Dispatcher> StaDispatcher = new(() => WpfTestHost.Dispatcher);
    private readonly string tempRoot;

    public NeighborHostItemViewModelInterconnectTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public void InterconnectCommands_ShouldRemainEnabled_WhenHostDoesNotExposePort7727()
    {
        RunOnStaThread(() =>
        {
            EnsureApplication();
            var viewModel = CreateViewModel(new NeighborHost { IpAddress = "10.0.0.8", IsInterconnectOpen = false }, out _, out _);

            Assert.True(viewModel.HasInterconnectActions);
            Assert.True(viewModel.SendTextCommand.CanExecute(null));
            Assert.True(viewModel.SendFileCommand.CanExecute(null));

            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SendFileCommand_ShouldRejectOversizedFileBeforeNetworkSend()
    {
        RunOnStaThread(async () =>
        {
            EnsureApplication();
            var filePath = Path.Combine(tempRoot, "too-large.bin");
            await using (var stream = new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.SetLength(InterconnectLimits.MaxFileSizeBytes + 1);
            }

            var viewModel = CreateViewModel(
                new NeighborHost { IpAddress = "10.0.0.9", IsInterconnectOpen = true },
                out var messageService,
                out var interconnectService,
                filePath);

            viewModel.SendFileCommand.Execute(null);
            await Task.Delay(150);

            Assert.Equal(0, interconnectService.SendFileCallCount);
            Assert.Contains(messageService.Errors, item => item.Contains("500MB", StringComparison.OrdinalIgnoreCase));
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static NeighborHostItemViewModel CreateViewModel(NeighborHost host, out RecordingMessageService messageService, out RecordingInterconnectService interconnectService, string? sendFilePath = null)
    {
        messageService = new RecordingMessageService();
        interconnectService = new RecordingInterconnectService();
        return new NeighborHostItemViewModel(
            host,
            "meta.json",
            _ => { },
            messageService,
            new RecordingSystemInteractionService(),
            new TestTextInputDialogService(),
            interconnectService,
            new TestFileDialogService(sendFilePath));
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

    private sealed class RecordingMessageService : IMessageService
    {
        public List<string> Errors { get; } = [];
        public void ShowInfo(string message, string title) { }
        public void ShowError(string message, string title) => Errors.Add(message);
        public bool Confirm(string message, string title) => true;
        public ExitConfirmationResult ConfirmExitWithActiveTunnels(string message, string title) => ExitConfirmationResult.Cancel;
    }

    private sealed class RecordingInterconnectService : IInterconnectService
    {
        public int SendFileCallCount { get; private set; }
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
        public Task SendFileAsync(string ipAddress, int port, string filePath, IProgress<InterconnectSendProgress>? progress, CancellationToken cancellationToken)
        {
            SendFileCallCount++;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSystemInteractionService : ISystemInteractionService
    {
        public void CopyText(string text) { }
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
            value = "hello";
            return true;
        }

        public bool TryShowMultiline(string title, string prompt, string initialValue, out string value)
        {
            value = "hello";
            return true;
        }
    }

    private sealed class TestFileDialogService : IFileDialogService
    {
        private readonly string? sendFilePath;

        public TestFileDialogService(string? sendFilePath)
        {
            this.sendFilePath = sendFilePath;
        }

        public string? PickImportPath() => null;
        public string? PickExportPath(string tunnelName) => null;
        public string? PickSendFilePath() => sendFilePath;
    }
}
