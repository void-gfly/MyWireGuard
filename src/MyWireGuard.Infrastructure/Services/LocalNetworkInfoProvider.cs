using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Services;

public sealed record LocalNetworkInterfaceSnapshot(
    NetworkInterfaceType NetworkInterfaceType,
    OperationalStatus OperationalStatus,
    IReadOnlyList<string> IpAddresses);

public static class LocalNetworkInfoProvider
{
    private static readonly NetworkInterfaceType[] PhysicalLanInterfaceTypes =
    [
        NetworkInterfaceType.Ethernet,
        NetworkInterfaceType.GigabitEthernet,
        NetworkInterfaceType.Wireless80211
    ];

    public static InterconnectLocalInfo GetLocalInfo()
    {
        return new InterconnectLocalInfo(Environment.MachineName, GetLanIpAddresses(GetInterfaceSnapshots()));
    }

    public static IReadOnlyList<string> GetLanIpAddresses(IEnumerable<LocalNetworkInterfaceSnapshot> interfaces)
    {
        return interfaces
            .Where(item => item.OperationalStatus == OperationalStatus.Up)
            .Where(item => PhysicalLanInterfaceTypes.Contains(item.NetworkInterfaceType))
            .SelectMany(item => item.IpAddresses)
            .Where(IsUsableLanIpv4Address)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => IPAddress.Parse(address), new IpAddressComparer())
            .ToArray();
    }

    private static IReadOnlyList<LocalNetworkInterfaceSnapshot> GetInterfaceSnapshots()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Select(item => new LocalNetworkInterfaceSnapshot(
                item.NetworkInterfaceType,
                item.OperationalStatus,
                item.GetIPProperties()
                    .UnicastAddresses
                    .Where(address => address.Address.AddressFamily == AddressFamily.InterNetwork)
                    .Select(address => address.Address.ToString())
                    .ToArray()))
            .ToArray();
    }

    private static bool IsUsableLanIpv4Address(string value)
    {
        if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes is not [127, ..]
            && bytes is not [169, 254, ..]
            && !address.Equals(IPAddress.Any);
    }

    private sealed class IpAddressComparer : IComparer<IPAddress>
    {
        public int Compare(IPAddress? x, IPAddress? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var left = x.GetAddressBytes();
            var right = y.GetAddressBytes();
            for (var index = 0; index < left.Length; index++)
            {
                var compare = left[index].CompareTo(right[index]);
                if (compare != 0)
                {
                    return compare;
                }
            }

            return 0;
        }
    }
}
