using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IInterconnectLocalInfoClient
{
    Task<InterconnectLocalInfo?> TryGetLocalInfoAsync(
        string ipAddress,
        int port,
        int timeoutMs,
        CancellationToken cancellationToken);
}
