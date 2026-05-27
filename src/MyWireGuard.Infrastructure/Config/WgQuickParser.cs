using System.Text;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Config;

public sealed class WgQuickParser
{
    public TunnelProfile Parse(string configText, string name)
    {
        var profile = new TunnelProfile
        {
            Name = name,
            ConfigText = NormalizeLineEndings(configText)
        };

        InterfaceConfig? currentInterface = null;
        PeerConfig? currentPeer = null;

        foreach (var rawLine in SplitLines(configText))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                var section = line[1..^1].Trim();
                if (section.Equals("Interface", StringComparison.OrdinalIgnoreCase))
                {
                    currentInterface = profile.Interface;
                    currentPeer = null;
                }
                else if (section.Equals("Peer", StringComparison.OrdinalIgnoreCase))
                {
                    currentPeer = new PeerConfig();
                    profile.Peers.Add(currentPeer);
                    currentInterface = null;
                }

                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (currentInterface is not null)
            {
                ApplyInterfaceValue(currentInterface, key, value);
            }
            else if (currentPeer is not null)
            {
                ApplyPeerValue(currentPeer, key, value);
            }
        }

        return profile;
    }

    public string Serialize(TunnelProfile profile)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Interface]");

        if (!string.IsNullOrWhiteSpace(profile.Interface.PrivateKey))
        {
            builder.AppendLine($"PrivateKey = {profile.Interface.PrivateKey}");
        }

        if (profile.Interface.Addresses.Count > 0)
        {
            builder.AppendLine($"Address = {string.Join(", ", profile.Interface.Addresses)}");
        }

        if (profile.Interface.DnsServers.Count > 0)
        {
            builder.AppendLine($"DNS = {string.Join(", ", profile.Interface.DnsServers)}");
        }

        if (profile.Interface.ListenPort.HasValue)
        {
            builder.AppendLine($"ListenPort = {profile.Interface.ListenPort.Value}");
        }

        if (profile.Interface.Mtu.HasValue)
        {
            builder.AppendLine($"MTU = {profile.Interface.Mtu.Value}");
        }

        foreach (var peer in profile.Peers)
        {
            builder.AppendLine();
            builder.AppendLine("[Peer]");

            if (!string.IsNullOrWhiteSpace(peer.PublicKey))
            {
                builder.AppendLine($"PublicKey = {peer.PublicKey}");
            }

            if (!string.IsNullOrWhiteSpace(peer.PresharedKey))
            {
                builder.AppendLine($"PresharedKey = {peer.PresharedKey}");
            }

            if (peer.AllowedIps.Count > 0)
            {
                builder.AppendLine($"AllowedIPs = {string.Join(", ", peer.AllowedIps)}");
            }

            if (!string.IsNullOrWhiteSpace(peer.Endpoint))
            {
                builder.AppendLine($"Endpoint = {peer.Endpoint}");
            }

            if (peer.PersistentKeepalive.HasValue)
            {
                builder.AppendLine($"PersistentKeepalive = {peer.PersistentKeepalive.Value}");
            }
        }

        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static IEnumerable<string> SplitLines(string content)
    {
        return NormalizeLineEndings(content).Split('\n');
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static void ApplyInterfaceValue(InterfaceConfig config, string key, string value)
    {
        switch (key.ToUpperInvariant())
        {
            case "PRIVATEKEY":
                config.PrivateKey = value;
                break;
            case "ADDRESS":
                ApplyList(config.Addresses, value);
                break;
            case "DNS":
                ApplyList(config.DnsServers, value);
                break;
            case "LISTENPORT":
                if (int.TryParse(value, out var listenPort))
                {
                    config.ListenPort = listenPort;
                }

                break;
            case "MTU":
                if (int.TryParse(value, out var mtu))
                {
                    config.Mtu = mtu;
                }

                break;
        }
    }

    private static void ApplyPeerValue(PeerConfig config, string key, string value)
    {
        switch (key.ToUpperInvariant())
        {
            case "PUBLICKEY":
                config.PublicKey = value;
                break;
            case "PRESHAREDKEY":
                config.PresharedKey = value;
                break;
            case "ALLOWEDIPS":
                ApplyList(config.AllowedIps, value);
                break;
            case "ENDPOINT":
                config.Endpoint = value;
                break;
            case "PERSISTENTKEEPALIVE":
                if (int.TryParse(value, out var keepalive))
                {
                    config.PersistentKeepalive = keepalive;
                }

                break;
        }
    }

    private static void ApplyList(ICollection<string> collection, string rawValue)
    {
        foreach (var value in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            collection.Add(value);
        }
    }
}