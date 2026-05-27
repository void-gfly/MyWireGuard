using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface INetworkNeighborScanner
{
    Task<NeighborScanResult> ScanAsync(
        TunnelProfile profile,
        NeighborMetadata existingMetadata,
        IProgress<NeighborScanProgress>? progress = null,
    IProgress<NeighborHostUpdate>? hostUpdateProgress = null,
        NeighborScanOptions? options = null,
        CancellationToken cancellationToken = default);
}