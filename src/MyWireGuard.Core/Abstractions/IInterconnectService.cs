using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IInterconnectService : IAsyncDisposable
{
    event EventHandler<InterconnectReceiveTextRecord>? TextReceived;

    event EventHandler<InterconnectReceiveFileRecord>? FileReceived;

    event EventHandler<InterconnectSendProgress>? SendProgressChanged;

    IReadOnlyList<InterconnectReceiveTextRecord> GetReceivedTextRecords();

    IReadOnlyList<InterconnectReceiveFileRecord> GetReceivedFileRecords();

    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync(CancellationToken cancellationToken);

    Task SendTextAsync(string ipAddress, int port, string text, CancellationToken cancellationToken);

    Task SendFileAsync(string ipAddress, int port, string filePath, IProgress<InterconnectSendProgress>? progress, CancellationToken cancellationToken);
}
