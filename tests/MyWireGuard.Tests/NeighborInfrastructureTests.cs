using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class NeighborInfrastructureTests
{
    private readonly IPv4SubnetCalculator calculator = new();

    [Fact]
    public void TryGetPrimarySubnet_UsesIpv4InterfaceAddress()
    {
        var profile = new TunnelProfile();
        profile.Interface.Addresses.Add("172.16.0.3/24");
        profile.Interface.Addresses.Add("fd00::2/64");

        var success = calculator.TryGetPrimarySubnet(profile, out var subnetCidr);

        Assert.True(success);
        Assert.Equal("172.16.0.0/24", subnetCidr);
    }

    [Fact]
    public void EnumerateHostAddresses_ReturnsUsableRange()
    {
        var hosts = calculator.EnumerateHostAddresses("172.16.0.0/24");

        Assert.Equal(254, hosts.Count);
        Assert.Equal("172.16.0.1", hosts[0]);
        Assert.Equal("172.16.0.254", hosts[^1]);
    }

    [Fact]
    public void EnumerateHostAddresses_RejectsSlash32()
    {
        Assert.Empty(calculator.EnumerateHostAddresses("172.16.0.3/32"));
    }

    [Fact]
    public async Task FileNeighborMetadataStore_RoundTripsMetadata()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var runtimePaths = CreateRuntimePaths(tempRoot);
            var store = new FileNeighborMetadataStore(runtimePaths);
            var metadata = new NeighborMetadata
            {
                TunnelName = "demo",
                SubnetCidr = "172.16.0.0/24",
                LastScanStartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                LastScanCompletedAt = DateTimeOffset.UtcNow
            };
            metadata.Hosts.Add(new NeighborHost
            {
                IpAddress = "172.16.0.10",
                Remark = "db-01",
                RemarkSource = NeighborRemarkSource.Manual,
                Hostname = "db-01.local",
                IsAlive = true,
                PingMs = 8,
                IsRdpOpen = true,
                LastSeenAt = DateTimeOffset.UtcNow,
                LastScannedAt = DateTimeOffset.UtcNow
            });

            await store.SaveAsync(metadata);
            var loaded = await store.GetAsync("demo");

            Assert.Equal("172.16.0.0/24", loaded.SubnetCidr);
            Assert.Single(loaded.Hosts);
            Assert.Equal("db-01", loaded.Hosts[0].Remark);
            Assert.Equal(NeighborRemarkSource.Manual, loaded.Hosts[0].RemarkSource);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public void MergeScanIntoMetadata_PreservesExistingNonEmptyRemarkWithoutManualFlag()
    {
        var metadata = new NeighborMetadata
        {
            TunnelName = "demo",
            SubnetCidr = "172.16.0.0/24"
        };
        metadata.Hosts.Add(new NeighborHost
        {
            IpAddress = "172.16.0.10",
            Remark = "custom-name",
            RemarkSource = NeighborRemarkSource.None,
            Hostname = "old-host"
        });

        var scanResult = new NeighborScanResult
        {
            TunnelName = "demo",
            SubnetCidr = "172.16.0.0/24",
            StartedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            CompletedAt = DateTimeOffset.UtcNow,
            Hosts =
            [
                new NeighborHost
                {
                    IpAddress = "172.16.0.10",
                    Hostname = "new-host",
                    IsAlive = true
                },
                new NeighborHost
                {
                    IpAddress = "172.16.0.11",
                    Hostname = "auto-name",
                    IsAlive = true
                }
            ]
        };

        var merged = NetworkNeighborScanner.MergeScanIntoMetadata(metadata, scanResult);

        Assert.Equal(2, merged.Hosts.Count);
        Assert.Equal("custom-name", merged.Hosts[0].Remark);
        Assert.Equal(NeighborRemarkSource.None, merged.Hosts[0].RemarkSource);
        Assert.Equal("auto-name", merged.Hosts[1].Remark);
        Assert.Equal(NeighborRemarkSource.AutoDiscovered, merged.Hosts[1].RemarkSource);
    }

    private static AppRuntimePaths CreateRuntimePaths(string rootPath)
    {
        Environment.SetEnvironmentVariable("LOCALAPPDATA", rootPath);
        return new AppRuntimePaths();
    }
}