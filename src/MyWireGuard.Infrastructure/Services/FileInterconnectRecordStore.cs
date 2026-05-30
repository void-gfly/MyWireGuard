using System.Text.Json;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;

namespace MyWireGuard.Infrastructure.Services;

public sealed class FileInterconnectRecordStore : IInterconnectRecordStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(InterconnectJson.Options)
    {
        WriteIndented = true
    };

    private readonly AppRuntimePaths runtimePaths;
    private readonly ILogService logService;

    public FileInterconnectRecordStore(AppRuntimePaths runtimePaths, ILogService logService)
    {
        this.runtimePaths = runtimePaths;
        this.logService = logService;
    }

    public Task<IReadOnlyList<InterconnectReceiveTextRecord>> GetTextRecordsAsync(CancellationToken cancellationToken = default)
    {
        return LoadRecordsAsync<InterconnectReceiveTextRecord>(GetTextRecordsPath(), cancellationToken);
    }

    public Task<IReadOnlyList<InterconnectReceiveFileRecord>> GetFileRecordsAsync(CancellationToken cancellationToken = default)
    {
        return LoadRecordsAsync<InterconnectReceiveFileRecord>(GetFileRecordsPath(), cancellationToken);
    }

    public Task SaveTextRecordsAsync(IReadOnlyList<InterconnectReceiveTextRecord> records, CancellationToken cancellationToken = default)
    {
        return SaveRecordsAsync(GetTextRecordsPath(), records, cancellationToken);
    }

    public Task SaveFileRecordsAsync(IReadOnlyList<InterconnectReceiveFileRecord> records, CancellationToken cancellationToken = default)
    {
        return SaveRecordsAsync(GetFileRecordsPath(), records, cancellationToken);
    }

    public string GetTextRecordsPath()
    {
        return Path.Combine(GetInterconnectDirectory(), "received-texts.json");
    }

    public string GetFileRecordsPath()
    {
        return Path.Combine(GetInterconnectDirectory(), "received-files.json");
    }

    private async Task<IReadOnlyList<TRecord>> LoadRecordsAsync<TRecord>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var records = await JsonSerializer.DeserializeAsync<List<TRecord>>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
            return records ?? [];
        }
        catch (JsonException exception)
        {
            logService.WriteError($"Failed to parse interconnect records '{path}': {exception.Message}");
            return [];
        }
        catch (IOException exception)
        {
            logService.WriteError($"Failed to read interconnect records '{path}': {exception.Message}");
            return [];
        }
    }

    private async Task SaveRecordsAsync<TRecord>(string path, IReadOnlyList<TRecord> records, CancellationToken cancellationToken)
    {
        runtimePaths.EnsureCreated();
        Directory.CreateDirectory(GetInterconnectDirectory());

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, records, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    private string GetInterconnectDirectory()
    {
        return Path.Combine(runtimePaths.BaseDirectory, "Interconnect");
    }
}