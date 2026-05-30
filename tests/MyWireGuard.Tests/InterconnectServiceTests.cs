using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class InterconnectServiceTests : IDisposable
{
    private readonly string tempRoot;

    public InterconnectServiceTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
    }

    [Fact]
    public async Task SendTextAsync_ShouldReceiveTextRecord()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, 7727);
        await service.StartAsync(CancellationToken.None);

        await service.SendTextAsync("127.0.0.1", 7727, "hello from test", CancellationToken.None);
        var received = await WaitForAsync(
            () => service.GetReceivedTextRecords().Count == 1,
            service.GetReceivedTextRecords,
            () => string.Join(Environment.NewLine, logService.GetEntries().Select(entry => $"{entry.Level}: {entry.Message}")));

        Assert.Single(received);
        Assert.Equal("hello from test", received[0].Text);
        Assert.Equal("127.0.0.1", received[0].SourceIpAddress);
    }

    [Fact]
    public async Task SendFileAsync_ShouldReceiveFileAndAvoidOverwrite()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        var sourcePath = Path.Combine(tempRoot, "demo.txt");
        await File.WriteAllTextAsync(sourcePath, "payload", CancellationToken.None);

        await using var service = new InterconnectService(logService, receiveDirectory, 7727);
        await service.StartAsync(CancellationToken.None);

        await service.SendFileAsync("127.0.0.1", 7727, sourcePath, null, CancellationToken.None);
        await service.SendFileAsync("127.0.0.1", 7727, sourcePath, null, CancellationToken.None);
        var received = await WaitForAsync(
            () => service.GetReceivedFileRecords().Count == 2,
            service.GetReceivedFileRecords,
            () => string.Join(Environment.NewLine, logService.GetEntries().Select(entry => $"{entry.Level}: {entry.Message}")));

        Assert.Equal(2, received.Count);
        Assert.All(received, item => Assert.True(File.Exists(item.SavedPath)));
        Assert.NotEqual(received[0].SavedPath, received[1].SavedPath);
        Assert.Contains(received, item => item.SavedPath.EndsWith("demo.txt", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(received, item => item.SavedPath.EndsWith("demo (1).txt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendFileAsync_ShouldRejectFileLargerThanLimit()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        var sourcePath = Path.Combine(tempRoot, "too-large.bin");
        await using (var stream = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            stream.SetLength(InterconnectLimits.MaxFileSizeBytes + 1);
        }

        await using var service = new InterconnectService(logService, receiveDirectory, 7727);
        await service.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendFileAsync("127.0.0.1", 7727, sourcePath, null, CancellationToken.None));

        Assert.Contains("500MB", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(service.GetReceivedFileRecords());
    }

    [Fact]
    public async Task StartAndStopAsync_ShouldUpdateListenerStatusAndPort()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, 7727);

        Assert.Equal("已停止", service.ListenerStatusText);
        Assert.Equal(7727, service.ListenerPort);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal("监听中", service.ListenerStatusText);
        Assert.Equal(7727, service.ListenerPort);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal("已停止", service.ListenerStatusText);
        Assert.Equal(7727, service.ListenerPort);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private static async Task<T> WaitForAsync<T>(Func<bool> predicate, Func<T> valueFactory, Func<string> debugFactory)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (predicate())
            {
                return valueFactory();
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Timed out waiting for interconnect activity.{Environment.NewLine}{debugFactory()}");
    }

    private sealed class TestLogService : ILogService
    {
        private readonly List<LogEntry> entries = [];

        public event EventHandler<LogEntry>? EntryWritten;

        public IReadOnlyList<LogEntry> GetEntries()
        {
            return entries.ToArray();
        }

        public void WriteInfo(string message)
        {
            Write("Info", message);
        }

        public void WriteError(string message)
        {
            Write("Error", message);
        }

        private void Write(string level, string message)
        {
            var entry = new LogEntry(DateTimeOffset.Now, level, message);
            entries.Add(entry);
            EntryWritten?.Invoke(this, entry);
        }
    }
}
