using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface ITunnelServiceManager
{
    Task<TunnelStatus> GetStatusAsync(string tunnelName, CancellationToken cancellationToken = default);

    Task EnsureServiceConfigurationAsync(TunnelProfile profile, CancellationToken cancellationToken = default);

    Task StartAsync(TunnelProfile profile, CancellationToken cancellationToken = default);

    Task StopAsync(string tunnelName, CancellationToken cancellationToken = default);

    Task RemoveAsync(string tunnelName, CancellationToken cancellationToken = default);

    string GetServiceName(string tunnelName);

    bool IsRuntimeAvailable();
}