using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class InterconnectRecordStoreTests : IDisposable
{
    private readonly string tempRoot;

    public InterconnectRecordStoreTests()
    {
        tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        Environment.SetEnvironmentVariable("LOCALAPPDATA", tempRoot);
    }

    [Fact]
    public async Task FileInterconnectRecordStore_ShouldRoundTripTextAndFileRecords()
    {
        var store = CreateStore();
        var textRecords = new[]
        {
            new InterconnectReceiveTextRecord(DateTimeOffset.Parse("2026-05-31T08:00:00+00:00"), "10.0.0.2", "hello"),
            new InterconnectReceiveTextRecord(DateTimeOffset.Parse("2026-05-31T08:01:00+00:00"), "10.0.0.3", "world")
        };
        var fileRecords = new[]
        {
            new InterconnectReceiveFileRecord(DateTimeOffset.Parse("2026-05-31T08:02:00+00:00"), "10.0.0.4", "demo.txt", 7, @"C:\Temp\demo.txt")
        };

        await store.SaveTextRecordsAsync(textRecords);
        await store.SaveFileRecordsAsync(fileRecords);

        var loadedTexts = await store.GetTextRecordsAsync();
        var loadedFiles = await store.GetFileRecordsAsync();

        Assert.Equal(textRecords, loadedTexts);
        Assert.Equal(fileRecords, loadedFiles);
    }

    [Fact]
    public async Task FileInterconnectRecordStore_ShouldReturnEmptyListWhenJsonIsInvalid()
    {
        var store = CreateStore();
        var path = store.GetTextRecordsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{ invalid json", CancellationToken.None);

        var loadedTexts = await store.GetTextRecordsAsync();

        Assert.Empty(loadedTexts);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }

    private FileInterconnectRecordStore CreateStore()
    {
        return new FileInterconnectRecordStore(new AppRuntimePaths(), new TestLogService());
    }

    private sealed class TestLogService : ILogService
    {
        public event EventHandler<LogEntry>? EntryWritten;

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
}