namespace MyWireGuard.Core.Models;

public sealed class InterfaceConfig
{
    public string PrivateKey { get; set; } = string.Empty;

    public List<string> Addresses { get; } = [];

    public List<string> DnsServers { get; } = [];

    public int? ListenPort { get; set; }

    public int? Mtu { get; set; }
}