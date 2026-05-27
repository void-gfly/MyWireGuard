using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IConfigStore
{
    Task<IReadOnlyList<TunnelProfile>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TunnelProfile?> GetAsync(string name, CancellationToken cancellationToken = default);

    Task<TunnelProfile> SaveAsync(TunnelProfile profile, CancellationToken cancellationToken = default);

    Task<TunnelProfile> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);

    Task ExportAsync(string name, string destinationPath, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    string GetConfigPath(string name);
}