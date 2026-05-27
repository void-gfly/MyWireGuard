using System.Net;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Services;

public sealed class IPv4SubnetCalculator : IIPv4SubnetCalculator
{
    public bool TryGetPrimarySubnet(TunnelProfile profile, out string subnetCidr)
    {
        foreach (var address in profile.Interface.Addresses)
        {
            if (!TryNormalizeSubnet(address, out subnetCidr))
            {
                continue;
            }

            ParseCidr(subnetCidr, out _, out var prefixLength);
            if (prefixLength > 30)
            {
                continue;
            }

            return true;
        }

        subnetCidr = string.Empty;
        return false;
    }

    public IReadOnlyList<string> EnumerateHostAddresses(string subnetCidr)
    {
        if (!TryNormalizeSubnet(subnetCidr, out var normalizedCidr))
        {
            throw new InvalidOperationException($"Invalid IPv4 CIDR '{subnetCidr}'.");
        }

        ParseCidr(normalizedCidr, out var network, out var prefixLength);
        var hostBits = 32 - prefixLength;
        if (hostBits <= 1)
        {
            return Array.Empty<string>();
        }

        var networkValue = ToUInt32(network);
        var hostCount = (1u << hostBits) - 2u;
        var results = new List<string>(checked((int)hostCount));

        for (var index = 1u; index <= hostCount; index++)
        {
            results.Add(FromUInt32(networkValue + index).ToString());
        }

        return results;
    }

    internal static bool TryNormalizeSubnet(string rawAddress, out string subnetCidr)
    {
        subnetCidr = string.Empty;
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            return false;
        }

        var parts = rawAddress.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ipAddress) || ipAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var prefixLength) || prefixLength is < 0 or > 32)
        {
            return false;
        }

        var mask = prefixLength == 0 ? 0u : uint.MaxValue << (32 - prefixLength);
        var network = FromUInt32(ToUInt32(ipAddress) & mask);
        subnetCidr = $"{network}/{prefixLength}";
        return true;
    }

    private static void ParseCidr(string subnetCidr, out IPAddress network, out int prefixLength)
    {
        var parts = subnetCidr.Split('/', StringSplitOptions.TrimEntries);
        network = IPAddress.Parse(parts[0]);
        prefixLength = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value)
    {
        return new IPAddress([
            (byte)(value >> 24),
            (byte)(value >> 16),
            (byte)(value >> 8),
            (byte)value
        ]);
    }
}