using MyWireGuard.Infrastructure.Config;

namespace MyWireGuard.Tests;

public sealed class WgQuickParserTests
{
    private readonly WgQuickParser parser = new();

    [Fact]
    public void Parse_ShouldReadInterfaceAndPeerSections()
    {
        const string config = """
[Interface]
PrivateKey = private-key
Address = 10.0.0.2/32, fd00::2/128
DNS = 1.1.1.1, 8.8.8.8
ListenPort = 51820
MTU = 1420

[Peer]
PublicKey = peer-public
AllowedIPs = 0.0.0.0/0, ::/0
Endpoint = vpn.example.com:51820
PersistentKeepalive = 25
""";

        var profile = parser.Parse(config, "demo");

        Assert.Equal("demo", profile.Name);
        Assert.Equal("private-key", profile.Interface.PrivateKey);
        Assert.Equal(["10.0.0.2/32", "fd00::2/128"], profile.Interface.Addresses);
        Assert.Equal(["1.1.1.1", "8.8.8.8"], profile.Interface.DnsServers);
        Assert.Equal(51820, profile.Interface.ListenPort);
        Assert.Equal(1420, profile.Interface.Mtu);
        Assert.Single(profile.Peers);
        Assert.Equal("peer-public", profile.Peers[0].PublicKey);
        Assert.Equal(["0.0.0.0/0", "::/0"], profile.Peers[0].AllowedIps);
        Assert.Equal("vpn.example.com:51820", profile.Peers[0].Endpoint);
        Assert.Equal(25, profile.Peers[0].PersistentKeepalive);
    }

    [Fact]
    public void Serialize_ShouldEmitRoundTrippableConfig()
    {
        const string config = """
[Interface]
PrivateKey = private-key
Address = 10.0.0.2/32

[Peer]
PublicKey = peer-public
AllowedIPs = 10.0.0.0/24
""";

        var profile = parser.Parse(config, "demo");
        var serialized = parser.Serialize(profile);
        var reparsed = parser.Parse(serialized, "demo");

        Assert.Equal(profile.Interface.PrivateKey, reparsed.Interface.PrivateKey);
        Assert.Equal(profile.Interface.Addresses, reparsed.Interface.Addresses);
        Assert.Equal(profile.Peers[0].PublicKey, reparsed.Peers[0].PublicKey);
        Assert.Equal(profile.Peers[0].AllowedIps, reparsed.Peers[0].AllowedIps);
    }
}