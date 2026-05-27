namespace MyWireGuard.Core.Models;

public sealed class PeerConfig
{
    public string PublicKey { get; set; } = string.Empty;

    public string? PresharedKey { get; set; }

    public List<string> AllowedIps { get; } = [];

    public string? Endpoint { get; set; }

    public int? PersistentKeepalive { get; set; }
}