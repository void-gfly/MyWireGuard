namespace MyWireGuard.Core.Models;

public sealed class TunnelProfile
{
    public string Name { get; set; } = string.Empty;

    public string ConfigText { get; set; } = string.Empty;

    public string? ConfigPath { get; set; }

    public InterfaceConfig Interface { get; set; } = new();

    public List<PeerConfig> Peers { get; } = [];
}