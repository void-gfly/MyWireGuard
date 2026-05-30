using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Services;

public sealed class NetworkNeighborScanner : INetworkNeighborScanner
{
    private readonly IIPv4SubnetCalculator subnetCalculator;
    private readonly ILogService logService;
    private readonly IInterconnectLocalInfoClient localInfoClient;
    private readonly Func<string, CancellationToken, Task<string?>> hostnameResolver;

    public NetworkNeighborScanner(
        IIPv4SubnetCalculator subnetCalculator,
        ILogService logService,
        IInterconnectLocalInfoClient? localInfoClient = null,
        Func<string, CancellationToken, Task<string?>>? hostnameResolver = null)
    {
        this.subnetCalculator = subnetCalculator;
        this.logService = logService;
        this.localInfoClient = localInfoClient ?? new InterconnectLocalInfoClient();
        this.hostnameResolver = hostnameResolver ?? ResolveHostnameAsync;
    }

    public async Task<NeighborScanResult> ScanAsync(
        TunnelProfile profile,
        NeighborMetadata existingMetadata,
        IProgress<NeighborScanProgress>? progress = null,
        IProgress<NeighborHostUpdate>? hostUpdateProgress = null,
        NeighborScanOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new NeighborScanOptions();
        if (!subnetCalculator.TryGetPrimarySubnet(profile, out var subnetCidr))
        {
            throw new InvalidOperationException($"Tunnel '{profile.Name}' does not expose a scannable IPv4 subnet.");
        }

        var addresses = subnetCalculator.EnumerateHostAddresses(subnetCidr);
        var startedAt = DateTimeOffset.UtcNow;
        progress?.Report(new NeighborScanProgress
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            Phase = NeighborScanPhase.Ping,
            TotalHosts = addresses.Count
        });

        logService.WriteInfo($"Scanning subnet {subnetCidr} for tunnel '{profile.Name}'.");
        var aliveHosts = await PingAliveHostsAsync(profile.Name, subnetCidr, addresses, options, progress, hostUpdateProgress, cancellationToken).ConfigureAwait(false);
        var aliveMap = aliveHosts.ToDictionary(item => item.IpAddress, StringComparer.OrdinalIgnoreCase);

        progress?.Report(new NeighborScanProgress
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            Phase = NeighborScanPhase.Ports,
            TotalHosts = aliveHosts.Count,
            AliveHosts = aliveHosts.Count
        });
        await ScanPortsAsync(aliveHosts, options, progress, hostUpdateProgress, profile.Name, subnetCidr, cancellationToken).ConfigureAwait(false);

        progress?.Report(new NeighborScanProgress
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            Phase = NeighborScanPhase.Hostnames,
            TotalHosts = aliveHosts.Count,
            AliveHosts = aliveHosts.Count
        });
        await ResolveHostnamesAsync(aliveHosts, options, progress, hostUpdateProgress, profile.Name, subnetCidr, cancellationToken).ConfigureAwait(false);

        var mergedHosts = MergeMetadata(existingMetadata, subnetCidr, aliveMap);
        var completedAt = DateTimeOffset.UtcNow;

        progress?.Report(new NeighborScanProgress
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            Phase = NeighborScanPhase.Completed,
            TotalHosts = addresses.Count,
            ProcessedHosts = addresses.Count,
            AliveHosts = aliveHosts.Count
        });

        return new NeighborScanResult
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            Hosts = mergedHosts
        };
    }

    public static NeighborMetadata MergeScanIntoMetadata(NeighborMetadata existingMetadata, NeighborScanResult result)
    {
        var aliveMap = result.Hosts.ToDictionary(host => host.IpAddress, StringComparer.OrdinalIgnoreCase);
        var mergedHosts = MergeMetadata(existingMetadata, result.SubnetCidr, aliveMap);
        var metadata = new NeighborMetadata
        {
            TunnelName = result.TunnelName,
            SubnetCidr = result.SubnetCidr,
            LastScanStartedAt = result.StartedAt,
            LastScanCompletedAt = result.CompletedAt
        };

        foreach (var host in mergedHosts)
        {
            metadata.Hosts.Add(host);
        }

        return metadata;
    }

    private async Task<List<NeighborHost>> PingAliveHostsAsync(
        string tunnelName,
        string subnetCidr,
        IReadOnlyList<string> addresses,
        NeighborScanOptions options,
        IProgress<NeighborScanProgress>? progress,
        IProgress<NeighborHostUpdate>? hostUpdateProgress,
        CancellationToken cancellationToken)
    {
        var aliveHosts = new ConcurrentBag<NeighborHost>();
        var processedHosts = 0;

        await Parallel.ForEachAsync(addresses, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.PingConcurrency
        }, async (address, ct) =>
        {
            var probe = await PingAsync(address, options.PingTimeoutMs, ct).ConfigureAwait(false);
            if (probe is not null)
            {
                aliveHosts.Add(probe);
                hostUpdateProgress?.Report(new NeighborHostUpdate
                {
                    Phase = NeighborScanPhase.Ping,
                    Host = CloneHost(probe)
                });
            }

            var completed = Interlocked.Increment(ref processedHosts);
            progress?.Report(new NeighborScanProgress
            {
                TunnelName = tunnelName,
                SubnetCidr = subnetCidr,
                Phase = NeighborScanPhase.Ping,
                TotalHosts = addresses.Count,
                ProcessedHosts = completed,
                AliveHosts = aliveHosts.Count
            });
        }).ConfigureAwait(false);

        return aliveHosts.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()).ToList();
    }

    private async Task ScanPortsAsync(
        IReadOnlyList<NeighborHost> aliveHosts,
        NeighborScanOptions options,
        IProgress<NeighborScanProgress>? progress,
        IProgress<NeighborHostUpdate>? hostUpdateProgress,
        string tunnelName,
        string subnetCidr,
        CancellationToken cancellationToken)
    {
        var processedHosts = 0;
        await Parallel.ForEachAsync(aliveHosts, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.PortConcurrency
        }, async (host, ct) =>
        {
            foreach (var port in options.Ports)
            {
                var isOpen = await IsPortOpenAsync(host.IpAddress, port, options.PortTimeoutMs, ct).ConfigureAwait(false);
                if (port == 3389)
                {
                    host.IsRdpOpen = isOpen;
                }
                else if (port == 22)
                {
                    host.IsSshOpen = isOpen;
                }
                else if (port == InterconnectLimits.DefaultPort)
                {
                    host.IsInterconnectOpen = isOpen;
                }
            }

            hostUpdateProgress?.Report(new NeighborHostUpdate
            {
                Phase = NeighborScanPhase.Ports,
                Host = CloneHost(host)
            });

            var completed = Interlocked.Increment(ref processedHosts);
            progress?.Report(new NeighborScanProgress
            {
                TunnelName = tunnelName,
                SubnetCidr = subnetCidr,
                Phase = NeighborScanPhase.Ports,
                TotalHosts = aliveHosts.Count,
                ProcessedHosts = completed,
                AliveHosts = aliveHosts.Count
            });
        }).ConfigureAwait(false);
    }

    private async Task ResolveHostnamesAsync(
        IReadOnlyList<NeighborHost> aliveHosts,
        NeighborScanOptions options,
        IProgress<NeighborScanProgress>? progress,
        IProgress<NeighborHostUpdate>? hostUpdateProgress,
        string tunnelName,
        string subnetCidr,
        CancellationToken cancellationToken)
    {
        var processedHosts = 0;
        await Parallel.ForEachAsync(aliveHosts, new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = options.HostnameConcurrency
        }, async (host, ct) =>
        {
            var localInfo = await localInfoClient
                .TryGetLocalInfoAsync(host.IpAddress, InterconnectLimits.DefaultPort, options.PortTimeoutMs, ct)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(localInfo?.ComputerName))
            {
                host.Hostname = localInfo.ComputerName;
                host.IsInterconnectOpen = true;
            }
            else
            {
                host.Hostname = await hostnameResolver(host.IpAddress, ct).ConfigureAwait(false);
            }

            hostUpdateProgress?.Report(new NeighborHostUpdate
            {
                Phase = NeighborScanPhase.Hostnames,
                Host = CloneHost(host)
            });
            var completed = Interlocked.Increment(ref processedHosts);
            progress?.Report(new NeighborScanProgress
            {
                TunnelName = tunnelName,
                SubnetCidr = subnetCidr,
                Phase = NeighborScanPhase.Hostnames,
                TotalHosts = aliveHosts.Count,
                ProcessedHosts = completed,
                AliveHosts = aliveHosts.Count
            });
        }).ConfigureAwait(false);
    }

    private static List<NeighborHost> MergeMetadata(NeighborMetadata existingMetadata, string subnetCidr, IReadOnlyDictionary<string, NeighborHost> aliveMap)
    {
        var mergedHosts = new List<NeighborHost>();
        var now = DateTimeOffset.UtcNow;

        foreach (var existingHost in existingMetadata.Hosts.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()))
        {
            if (aliveMap.TryGetValue(existingHost.IpAddress, out var liveHost))
            {
                ApplyExistingRemark(existingHost, liveHost);
                liveHost.LastSeenAt = now;
                liveHost.LastScannedAt = now;
                mergedHosts.Add(liveHost);
                continue;
            }

            mergedHosts.Add(new NeighborHost
            {
                IpAddress = existingHost.IpAddress,
                Remark = existingHost.Remark,
                RemarkSource = existingHost.RemarkSource,
                Hostname = existingHost.Hostname,
                IsAlive = false,
                PingMs = null,
                IsRdpOpen = false,
                IsSshOpen = false,
                IsInterconnectOpen = false,
                LastSeenAt = existingHost.LastSeenAt,
                LastScannedAt = now
            });
        }

        foreach (var liveHost in aliveMap.Values.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()))
        {
            if (mergedHosts.Any(existing => string.Equals(existing.IpAddress, liveHost.IpAddress, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(liveHost.Remark) && !string.IsNullOrWhiteSpace(liveHost.Hostname))
            {
                liveHost.Remark = liveHost.Hostname;
                liveHost.RemarkSource = NeighborRemarkSource.AutoDiscovered;
            }

            liveHost.LastSeenAt = now;
            liveHost.LastScannedAt = now;
            mergedHosts.Add(liveHost);
        }

        return mergedHosts.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()).ToList();
    }

    private static void ApplyExistingRemark(NeighborHost existingHost, NeighborHost liveHost)
    {
        if (!string.IsNullOrWhiteSpace(existingHost.Remark))
        {
            liveHost.Remark = existingHost.Remark;
            liveHost.RemarkSource = existingHost.RemarkSource;
            return;
        }

        if (string.IsNullOrWhiteSpace(liveHost.Remark) && !string.IsNullOrWhiteSpace(liveHost.Hostname))
        {
            liveHost.Remark = liveHost.Hostname;
            liveHost.RemarkSource = NeighborRemarkSource.AutoDiscovered;
        }
    }

    private static NeighborHost CloneHost(NeighborHost host)
    {
        return new NeighborHost
        {
            IpAddress = host.IpAddress,
            Remark = host.Remark,
            RemarkSource = host.RemarkSource,
            Hostname = host.Hostname,
            IsAlive = host.IsAlive,
            PingMs = host.PingMs,
            IsRdpOpen = host.IsRdpOpen,
            IsSshOpen = host.IsSshOpen,
            IsInterconnectOpen = host.IsInterconnectOpen,
            LastSeenAt = host.LastSeenAt,
            LastScannedAt = host.LastScannedAt
        };
    }

    private static async Task<NeighborHost?> PingAsync(string ipAddress, int timeoutMs, CancellationToken cancellationToken)
    {
        using var ping = new Ping();
        try
        {
            var reply = await ping.SendPingAsync(IPAddress.Parse(ipAddress), timeoutMs).WaitAsync(cancellationToken).ConfigureAwait(false);
            if (reply.Status != IPStatus.Success)
            {
                return null;
            }

            return new NeighborHost
            {
                IpAddress = ipAddress,
                IsAlive = true,
                PingMs = checked((int)reply.RoundtripTime)
            };
        }
        catch (PingException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async Task<bool> IsPortOpenAsync(string ipAddress, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            await tcpClient.ConnectAsync(IPAddress.Parse(ipAddress), port, timeoutCts.Token).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static async Task<string?> ResolveHostnameAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            var entry = await Dns.GetHostEntryAsync(ipAddress).WaitAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(entry.HostName) ? null : entry.HostName;
        }
        catch (SocketException)
        {
            return null;
        }
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
