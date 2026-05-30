using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IInterconnectRecordStore
{
    Task<IReadOnlyList<InterconnectReceiveTextRecord>> GetTextRecordsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InterconnectReceiveFileRecord>> GetFileRecordsAsync(CancellationToken cancellationToken = default);

    Task SaveTextRecordsAsync(IReadOnlyList<InterconnectReceiveTextRecord> records, CancellationToken cancellationToken = default);

    Task SaveFileRecordsAsync(IReadOnlyList<InterconnectReceiveFileRecord> records, CancellationToken cancellationToken = default);
}