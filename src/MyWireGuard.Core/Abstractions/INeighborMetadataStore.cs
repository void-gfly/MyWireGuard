using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface INeighborMetadataStore
{
    string GetPath(string tunnelName);

    Task<NeighborMetadata> GetAsync(string tunnelName, CancellationToken cancellationToken = default);

    Task SaveAsync(NeighborMetadata metadata, CancellationToken cancellationToken = default);
}