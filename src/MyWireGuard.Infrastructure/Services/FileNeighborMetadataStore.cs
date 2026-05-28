using System.Text.Json;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;

namespace MyWireGuard.Infrastructure.Services;

public sealed class FileNeighborMetadataStore : INeighborMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly AppRuntimePaths runtimePaths;

    public FileNeighborMetadataStore(AppRuntimePaths runtimePaths)
    {
        this.runtimePaths = runtimePaths;
    }

    public async Task<NeighborMetadata> GetAsync(string tunnelName, CancellationToken cancellationToken = default)
    {
        var path = GetPath(tunnelName);
        if (!File.Exists(path))
        {
            return new NeighborMetadata { TunnelName = tunnelName };
        }

        await using var stream = File.OpenRead(path);
        var metadata = await JsonSerializer.DeserializeAsync<NeighborMetadataDto>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return metadata?.ToModel(tunnelName) ?? new NeighborMetadata { TunnelName = tunnelName };
    }

    public async Task SaveAsync(NeighborMetadata metadata, CancellationToken cancellationToken = default)
    {
        runtimePaths.EnsureCreated();
        Directory.CreateDirectory(GetMetadataDirectory());

        var path = GetPath(metadata.TunnelName);
        var dto = NeighborMetadataDto.FromModel(metadata);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, dto, SerializerOptions, cancellationToken).ConfigureAwait(false);
    }

    public string GetPath(string tunnelName)
    {
        return Path.Combine(GetMetadataDirectory(), NormalizeName(tunnelName) + ".neighbors.json");
    }

    private string GetMetadataDirectory()
    {
        return Path.Combine(runtimePaths.BaseDirectory, "Neighbors");
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Tunnel name cannot be empty.");
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return sanitized.Replace(' ', '_');
    }

    private sealed class NeighborMetadataDto
    {
        public string TunnelName { get; set; } = string.Empty;

        public string? SubnetCidr { get; set; }

        public DateTimeOffset? LastScanStartedAt { get; set; }

        public DateTimeOffset? LastScanCompletedAt { get; set; }

        public List<NeighborHostDto> Hosts { get; set; } = [];

        public NeighborMetadata ToModel(string fallbackTunnelName)
        {
            var metadata = new NeighborMetadata
            {
                TunnelName = string.IsNullOrWhiteSpace(TunnelName) ? fallbackTunnelName : TunnelName,
                SubnetCidr = SubnetCidr,
                LastScanStartedAt = LastScanStartedAt,
                LastScanCompletedAt = LastScanCompletedAt
            };

            foreach (var host in Hosts)
            {
                metadata.Hosts.Add(host.ToModel());
            }

            return metadata;
        }

        public static NeighborMetadataDto FromModel(NeighborMetadata metadata)
        {
            return new NeighborMetadataDto
            {
                TunnelName = metadata.TunnelName,
                SubnetCidr = metadata.SubnetCidr,
                LastScanStartedAt = metadata.LastScanStartedAt,
                LastScanCompletedAt = metadata.LastScanCompletedAt,
                Hosts = metadata.Hosts.Select(NeighborHostDto.FromModel).ToList()
            };
        }
    }

    private sealed class NeighborHostDto
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

        public NeighborHost ToModel()
        {
            return new NeighborHost
            {
                IpAddress = IpAddress,
                Remark = Remark,
                RemarkSource = RemarkSource,
                Hostname = Hostname,
                IsAlive = IsAlive,
                PingMs = PingMs,
                IsRdpOpen = IsRdpOpen,
                IsSshOpen = IsSshOpen,
                LastSeenAt = LastSeenAt,
                LastScannedAt = LastScannedAt
            };
        }

        public static NeighborHostDto FromModel(NeighborHost host)
        {
            return new NeighborHostDto
            {
                IpAddress = host.IpAddress,
                Remark = host.Remark,
                RemarkSource = host.RemarkSource,
                Hostname = host.Hostname,
                IsAlive = host.IsAlive,
                PingMs = host.PingMs,
                IsRdpOpen = host.IsRdpOpen,
                IsSshOpen = host.IsSshOpen,
                LastSeenAt = host.LastSeenAt,
                LastScannedAt = host.LastScannedAt
            };
        }
    }
}