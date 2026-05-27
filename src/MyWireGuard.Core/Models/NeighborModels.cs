namespace MyWireGuard.Core.Models;

public enum NeighborRemarkSource
{
    None = 0,
    AutoDiscovered = 1,
    Manual = 2
}

public enum NeighborScanPhase
{
    Idle = 0,
    Ping = 1,
    Ports = 2,
    Hostnames = 3,
    Completed = 4
}

public sealed class NeighborHost
{
    public string IpAddress { get; set; } = string.Empty;

    public string? Remark { get; set; }

    public NeighborRemarkSource RemarkSource { get; set; }

    public string? Hostname { get; set; }

    public bool IsAlive { get; set; }

    public int? PingMs { get; set; }

    public bool IsRdpOpen { get; set; }

    public bool IsSshOpen { get; set; }

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? LastScannedAt { get; set; }
}

public sealed class NeighborMetadata
{
    public string TunnelName { get; set; } = string.Empty;

    public string? SubnetCidr { get; set; }

    public DateTimeOffset? LastScanStartedAt { get; set; }

    public DateTimeOffset? LastScanCompletedAt { get; set; }

    public List<NeighborHost> Hosts { get; } = [];
}

public sealed class NeighborScanProgress
{
    public string TunnelName { get; set; } = string.Empty;

    public string? SubnetCidr { get; set; }

    public NeighborScanPhase Phase { get; set; }

    public int TotalHosts { get; set; }

    public int ProcessedHosts { get; set; }

    public int AliveHosts { get; set; }
}

public sealed class NeighborHostUpdate
{
    public NeighborScanPhase Phase { get; set; }

    public NeighborHost Host { get; set; } = new();
}

public sealed class NeighborScanOptions
{
    public int PingTimeoutMs { get; set; } = 300;

    public int PortTimeoutMs { get; set; } = 400;

    public int PingConcurrency { get; set; } = 32;

    public int PortConcurrency { get; set; } = 16;

    public int HostnameConcurrency { get; set; } = 8;

    public int[] Ports { get; set; } = [3389, 22];
}

public sealed class NeighborScanResult
{
    public string TunnelName { get; set; } = string.Empty;

    public string SubnetCidr { get; set; } = string.Empty;

    public IReadOnlyList<NeighborHost> Hosts { get; init; } = Array.Empty<NeighborHost>();

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset CompletedAt { get; set; }
}