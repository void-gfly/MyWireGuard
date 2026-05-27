using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IIPv4SubnetCalculator
{
    bool TryGetPrimarySubnet(TunnelProfile profile, out string subnetCidr);

    IReadOnlyList<string> EnumerateHostAddresses(string subnetCidr);
}