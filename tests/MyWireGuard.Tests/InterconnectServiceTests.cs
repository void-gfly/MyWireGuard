using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Services;
using System.Text.Json;

namespace MyWireGuard.Tests;

public sealed class InterconnectServiceTests : IDisposable
{
    private const byte TextMessageType = 1;
    private const byte FileMessageType = 2;
    private static int nextPort = 20000;
    private readonly string tempRoot;
    private readonly int port;

    public InterconnectServiceTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        port = Interlocked.Increment(ref nextPort);
    }

    [Fact]
    public async Task SendTextAsync_ShouldReceiveTextRecord()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        await service.SendTextAsync("127.0.0.1", port, "hello from test", CancellationToken.None);
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

        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        await service.SendFileAsync("127.0.0.1", port, sourcePath, null, CancellationToken.None);
        await service.SendFileAsync("127.0.0.1", port, sourcePath, null, CancellationToken.None);
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

        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendFileAsync("127.0.0.1", port, sourcePath, null, CancellationToken.None));

        Assert.Contains("500MB", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(service.GetReceivedFileRecords());
    }

    [Fact]
    public async Task SendTextAsync_ShouldIncludeTargetAndSocketCodeWhenConnectFails()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendTextAsync("127.0.0.1", port, "hello", CancellationToken.None));

        Assert.Contains($"127.0.0.1:{port}", exception.Message, StringComparison.Ordinal);
        Assert.Contains("ConnectionRefused", exception.Message, StringComparison.Ordinal);
        Assert.Contains(logService.GetEntries(), entry =>
            entry.Level == "Error"
            && entry.Message.Contains($"127.0.0.1:{port}", StringComparison.Ordinal)
            && entry.Message.Contains("ConnectionRefused", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TryGetLocalInfoAsync_ShouldReturnCamelCaseJsonPayload()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);
        var client = new InterconnectLocalInfoClient();

        var localInfo = await client.TryGetLocalInfoAsync("127.0.0.1", port, 1000, CancellationToken.None);

        Assert.NotNull(localInfo);
        Assert.False(string.IsNullOrWhiteSpace(localInfo.ComputerName));
        var json = JsonSerializer.Serialize(localInfo, InterconnectJson.Options);
        Assert.Contains("\"computerName\":", json);
        Assert.Contains("\"lanIpAddresses\":", json);
        Assert.DoesNotContain("\"ComputerName\":", json);
    }

    [Fact]
    public async Task StartAndStopAsync_ShouldUpdateListenerStatusAndPort()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);

        Assert.Equal("已停止", service.ListenerStatusText);
        Assert.Equal(port, service.ListenerPort);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal("监听中", service.ListenerStatusText);
        Assert.Equal(port, service.ListenerPort);

        await service.StopAsync(CancellationToken.None);

        Assert.Equal("已停止", service.ListenerStatusText);
        Assert.Equal(port, service.ListenerPort);
    }

    [Fact]
    public async Task StartAsync_ShouldRejectSecondListenerOnSamePort()
    {
        var firstReceiveDirectory = Path.Combine(tempRoot, "recv-first");
        var secondReceiveDirectory = Path.Combine(tempRoot, "recv-second");
        await using var firstService = new InterconnectService(new TestLogService(), firstReceiveDirectory, port);
        await firstService.StartAsync(CancellationToken.None);
        await using var secondService = new InterconnectService(new TestLogService(), secondReceiveDirectory, port);

        await Assert.ThrowsAnyAsync<SocketException>(() => secondService.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReceiveTextAsync_ShouldRejectInvalidPayloadLength()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        var payload = new byte[1 + sizeof(int)];
        payload[0] = TextMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), 0);

        await SendRawMessageAsync(port, payload);

        var entries = await WaitForAsync(
            () => logService.GetEntries().Any(entry => entry.Level == "Error" && entry.Message.Contains("接收文本长度无效", StringComparison.Ordinal)),
            logService.GetEntries,
            () => GetLogDebugInfo(logService));

        Assert.Empty(service.GetReceivedTextRecords());
        Assert.Contains(entries, entry => entry.Message.Contains("接收文本长度无效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReceiveFileAsync_ShouldRejectInvalidFileNameLength()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        var payload = new byte[1 + sizeof(int)];
        payload[0] = FileMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), 0);

        await SendRawMessageAsync(port, payload);

        var entries = await WaitForAsync(
            () => logService.GetEntries().Any(entry => entry.Level == "Error" && entry.Message.Contains("接收文件名长度无效", StringComparison.Ordinal)),
            logService.GetEntries,
            () => GetLogDebugInfo(logService));

        Assert.Empty(service.GetReceivedFileRecords());
        Assert.Contains(entries, entry => entry.Message.Contains("接收文件名长度无效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReceiveFileAsync_ShouldRejectNegativeFileLength()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        var payload = new byte[1 + sizeof(int) + 1 + sizeof(long)];
        payload[0] = FileMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), 1);
        payload[1 + sizeof(int)] = (byte)'a';
        BinaryPrimitives.WriteInt64LittleEndian(payload.AsSpan(1 + sizeof(int) + 1), -1);

        await SendRawMessageAsync(port, payload);

        var entries = await WaitForAsync(
            () => logService.GetEntries().Any(entry => entry.Level == "Error" && entry.Message.Contains("接收文件大小无效", StringComparison.Ordinal)),
            logService.GetEntries,
            () => GetLogDebugInfo(logService));

        Assert.Empty(service.GetReceivedFileRecords());
        Assert.Contains(entries, entry => entry.Message.Contains("接收文件大小无效", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReceiveTextAsync_ShouldRejectPrematureEndOfStream()
    {
        var logService = new TestLogService();
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        await using var service = new InterconnectService(logService, receiveDirectory, port);
        await service.StartAsync(CancellationToken.None);

        var payload = new byte[1 + sizeof(int) + 2];
        payload[0] = TextMessageType;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(1), 5);
        payload[1 + sizeof(int)] = (byte)'o';
        payload[1 + sizeof(int) + 1] = (byte)'k';

        await SendRawMessageAsync(port, payload);

        var entries = await WaitForAsync(
            () => logService.GetEntries().Any(entry => entry.Level == "Error" && entry.Message.Contains("互联消息数据提前结束", StringComparison.Ordinal)),
            logService.GetEntries,
            () => GetLogDebugInfo(logService));

        Assert.Empty(service.GetReceivedTextRecords());
        Assert.Contains(entries, entry => entry.Message.Contains("互联消息数据提前结束", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InterconnectService_ShouldLoadPersistedLatestTwentyRecordsAfterRestart()
    {
        var receiveDirectory = Path.Combine(tempRoot, "recv");
        var runtimePaths = CreateRuntimePaths(tempRoot);

        await using (var service = new InterconnectService(
            new TestLogService(),
            receiveDirectory,
            new FileInterconnectRecordStore(runtimePaths, new TestLogService()),
            port))
        {
            await service.StartAsync(CancellationToken.None);

            for (var index = 0; index < 25; index++)
            {
                var sourcePath = Path.Combine(tempRoot, $"file-{index}.txt");
                await File.WriteAllTextAsync(sourcePath, $"payload-{index}", CancellationToken.None);
                await service.SendTextAsync("127.0.0.1", port, $"text-{index}", CancellationToken.None);
                await service.SendFileAsync("127.0.0.1", port, sourcePath, null, CancellationToken.None);
            }

            await WaitForAsync(
                () => service.GetReceivedTextRecords().Count == 20
                    && service.GetReceivedTextRecords()[0].Text == "text-24"
                    && service.GetReceivedTextRecords()[^1].Text == "text-5"
                    && service.GetReceivedFileRecords().Count == 20
                    && service.GetReceivedFileRecords()[0].FileName == "file-24.txt"
                    && service.GetReceivedFileRecords()[^1].FileName == "file-5.txt",
                () => true,
                () => string.Join(Environment.NewLine, service.GetReceivedTextRecords().Select(record => record.Text)));
        }

        await using var restartedService = new InterconnectService(
            new TestLogService(),
            receiveDirectory,
            new FileInterconnectRecordStore(runtimePaths, new TestLogService()),
            7728);

        var loadedTexts = restartedService.GetReceivedTextRecords();
        var loadedFiles = restartedService.GetReceivedFileRecords();

        Assert.Equal(20, loadedTexts.Count);
        Assert.Equal("text-24", loadedTexts[0].Text);
        Assert.Equal("text-5", loadedTexts[^1].Text);
        Assert.Equal(20, loadedFiles.Count);
        Assert.Equal("file-24.txt", loadedFiles[0].FileName);
        Assert.Equal("file-5.txt", loadedFiles[^1].FileName);
        Assert.All(loadedFiles, record => Assert.True(File.Exists(record.SavedPath)));
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

    private static string GetLogDebugInfo(TestLogService logService)
    {
        return string.Join(Environment.NewLine, logService.GetEntries().Select(entry => $"{entry.Level}: {entry.Message}"));
    }

    private static AppRuntimePaths CreateRuntimePaths(string rootPath)
    {
        return new AppRuntimePaths(Path.Combine(rootPath, "MyWireGuard"));
    }

    private static async Task SendRawMessageAsync(int port, ReadOnlyMemory<byte> payload)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port, CancellationToken.None);
        await using var stream = client.GetStream();
        await stream.WriteAsync(payload, CancellationToken.None);
        await stream.FlushAsync(CancellationToken.None);
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
