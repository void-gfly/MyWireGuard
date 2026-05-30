using System.Collections.ObjectModel;
using System.Net;
using System.Text.Json;
using System.Windows;
using MyWireGuard.App.Services;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.App.ViewModels;

public sealed class TunnelNeighborsViewModel : ObservableObject, IDisposable
{
    private static readonly JsonSerializerOptions RemarkJsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly INeighborMetadataStore metadataStore;
    private readonly INetworkNeighborScanner scanner;
    private readonly IIPv4SubnetCalculator subnetCalculator;
    private readonly ILogService logService;
    private readonly IMessageService messageService;
    private readonly ISystemInteractionService systemInteractionService;
    private readonly ITextInputDialogService textInputDialogService;
    private readonly IInterconnectService interconnectService;
    private readonly IFileDialogService fileDialogService;
    private readonly Dictionary<string, NeighborHostItemViewModel> hostIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly object scanSessionLock = new();
    private TunnelProfile? currentProfile;
    private CancellationTokenSource? scanCancellationTokenSource;
    private long scanSessionVersion;
    private string subnetDisplay = "-";
    private string phaseDisplay = "未扫描";
    private string lastScanDisplay = "尚未扫描";
    private string metadataFilePath = string.Empty;
    private bool hasScannableSubnet;
    private bool isScanning;
    private string emptyStateText = "请选择一个隧道。";
    private NeighborMetadata? currentMetadata;
    private bool isConnected;

    public TunnelNeighborsViewModel(
        INeighborMetadataStore metadataStore,
        INetworkNeighborScanner scanner,
        IIPv4SubnetCalculator subnetCalculator,
        ILogService logService,
        IMessageService messageService,
        ISystemInteractionService systemInteractionService,
        ITextInputDialogService textInputDialogService,
        IInterconnectService interconnectService,
        IFileDialogService fileDialogService)
    {
        this.metadataStore = metadataStore;
        this.scanner = scanner;
        this.subnetCalculator = subnetCalculator;
        this.logService = logService;
        this.messageService = messageService;
        this.systemInteractionService = systemInteractionService;
        this.textInputDialogService = textInputDialogService;
        this.interconnectService = interconnectService;
        this.fileDialogService = fileDialogService;

        ScanCommand = new AsyncRelayCommand(ScanAsync, CanScan);
        CancelScanCommand = new AsyncRelayCommand(CancelScanAsync, () => IsScanning);
        ToggleScanCommand = new AsyncRelayCommand(ToggleScanAsync);
        CopySubnetRemarksCommand = new AsyncRelayCommand(CopySubnetRemarksAsync);
        ImportRemarksFromClipboardCommand = new AsyncRelayCommand(ImportRemarksFromClipboardAsync);
    }

    public ObservableCollection<NeighborHostItemViewModel> Hosts { get; } = [];

    public AsyncRelayCommand ScanCommand { get; }

    public AsyncRelayCommand CancelScanCommand { get; }

    public AsyncRelayCommand ToggleScanCommand { get; }

    public AsyncRelayCommand CopySubnetRemarksCommand { get; }

    public AsyncRelayCommand ImportRemarksFromClipboardCommand { get; }

    public string ScanButtonContent => IsScanning ? "取消" : "扫描网段";

    public string SubnetDisplay
    {
        get => subnetDisplay;
        private set => SetProperty(ref subnetDisplay, value);
    }

    public string PhaseDisplay
    {
        get => phaseDisplay;
        private set => SetProperty(ref phaseDisplay, value);
    }

    public string LastScanDisplay
    {
        get => lastScanDisplay;
        private set => SetProperty(ref lastScanDisplay, value);
    }

    public bool HasScannableSubnet
    {
        get => hasScannableSubnet;
        private set
        {
            if (SetProperty(ref hasScannableSubnet, value))
            {
                NotifyCommandStateChanged();
            }
        }
    }

    public bool IsScanning
    {
        get => isScanning;
        private set
        {
            if (SetProperty(ref isScanning, value))
            {
                RaisePropertyChanged(nameof(StatusSummary));
                RaisePropertyChanged(nameof(ScanButtonContent));
                NotifyCommandStateChanged();
            }
        }
    }

    public string EmptyStateText
    {
        get => emptyStateText;
        private set => SetProperty(ref emptyStateText, value);
    }

    public int OnlineCount => Hosts.Count(host => host.IsAlive);

    public int OfflineCount => Hosts.Count(host => !host.IsAlive);

    public int UnassignedCount => Hosts.Count(host => string.IsNullOrWhiteSpace(host.Remark));

    public int HostnameResolvedCount => Hosts.Count(host => !string.IsNullOrWhiteSpace(host.Hostname));

    public string StatusSummary => IsScanning ? $"{PhaseDisplay} · 在线 {OnlineCount}" : PhaseDisplay;

    public async Task LoadTunnelAsync(TunnelProfile? profile, bool isConnected)
    {
        CancelActiveScan();

        currentProfile = profile;
        this.isConnected = isConnected;
        metadataFilePath = profile is null ? string.Empty : metadataStore.GetPath(profile.Name);
        hostIndex.Clear();
        currentMetadata = null;

        await Application.Current.Dispatcher.InvokeAsync(Hosts.Clear);

        if (profile is null)
        {
            HasScannableSubnet = false;
            SubnetDisplay = "-";
            PhaseDisplay = "未扫描";
            LastScanDisplay = "尚未扫描";
            EmptyStateText = "请选择一个隧道。";
            RaiseSummaryPropertiesChanged();
            return;
        }

        currentMetadata = await metadataStore.GetAsync(profile.Name).ConfigureAwait(false);
        var hasSubnet = subnetCalculator.TryGetPrimarySubnet(profile, out var subnetCidr);
        HasScannableSubnet = hasSubnet;
        SubnetDisplay = hasSubnet ? subnetCidr : (currentMetadata.SubnetCidr ?? "-");
        LastScanDisplay = currentMetadata.LastScanCompletedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "尚未扫描";

        if (hasSubnet)
        {
            currentMetadata = await EnsureSubnetSnapshotAsync(profile, currentMetadata, subnetCidr).ConfigureAwait(false);
        }

        if (!hasSubnet)
        {
            PhaseDisplay = "不可扫描";
            EmptyStateText = "当前隧道没有可扫描的 IPv4 网段。";
            RaiseSummaryPropertiesChanged();
            return;
        }

        if (isConnected)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => ReplaceHosts(currentMetadata.Hosts));
            PhaseDisplay = currentMetadata.LastScanCompletedAt.HasValue ? "已扫描" : "待扫描";
            EmptyStateText = Hosts.Count == 0 ? "尚未发现主机。" : string.Empty;
            if (ShouldRunAutomaticScan(currentMetadata))
            {
                _ = RunAutomaticScanAsync();
            }
        }
        else
        {
            await Application.Current.Dispatcher.InvokeAsync(ClearVisibleHosts);
            PhaseDisplay = "未连接";
            EmptyStateText = "隧道未连接，连接后自动扫描。";
        }

        RaiseSummaryPropertiesChanged();
    }

    public async Task SetConnectionStateAsync(bool isConnected)
    {
        this.isConnected = isConnected;

        if (currentProfile is null)
        {
            return;
        }

        if (!HasScannableSubnet)
        {
            PhaseDisplay = "不可扫描";
            EmptyStateText = "当前隧道没有可扫描的 IPv4 网段。";
            return;
        }

        currentMetadata ??= await metadataStore.GetAsync(currentProfile.Name).ConfigureAwait(false);
        if (subnetCalculator.TryGetPrimarySubnet(currentProfile, out var subnetCidr))
        {
            currentMetadata = await EnsureSubnetSnapshotAsync(currentProfile, currentMetadata, subnetCidr).ConfigureAwait(false);
            SubnetDisplay = subnetCidr;
        }

        if (!isConnected)
        {
            CancelActiveScan();
            PhaseDisplay = "未连接";
            EmptyStateText = "隧道未连接，连接后自动扫描。";
            await Application.Current.Dispatcher.InvokeAsync(ClearVisibleHosts);
            RaiseSummaryPropertiesChanged();
            return;
        }

        await Application.Current.Dispatcher.InvokeAsync(() => ReplaceHosts(currentMetadata.Hosts));
        PhaseDisplay = currentMetadata.LastScanCompletedAt.HasValue ? "已扫描" : "待扫描";
        EmptyStateText = Hosts.Count == 0 ? "尚未发现主机。" : string.Empty;
        RaiseSummaryPropertiesChanged();
        if (ShouldRunAutomaticScan(currentMetadata))
        {
            _ = RunAutomaticScanAsync();
        }
    }

    public void Dispose()
    {
        CancelActiveScan();
    }

    private bool CanScan()
    {
        return currentProfile is not null && HasScannableSubnet && !IsScanning;
    }

    private async Task ScanAsync()
    {
        if (currentProfile is null || !isConnected || IsScanning)
        {
            return;
        }

        var profile = currentProfile;
        var tunnelName = profile.Name;
        var metadataPath = metadataStore.GetPath(tunnelName);
        using var scanCts = new CancellationTokenSource();
        var scanSessionId = BeginScanSession(scanCts);

        IsScanning = true;
        try
        {
            var metadata = await metadataStore.GetAsync(tunnelName, scanCts.Token).ConfigureAwait(false);
            if (!IsCurrentScanSession(scanSessionId, tunnelName))
            {
                return;
            }

            if (!subnetCalculator.TryGetPrimarySubnet(profile, out var subnetCidr))
            {
                HasScannableSubnet = false;
                PhaseDisplay = "不可扫描";
                return;
            }

            metadata = await EnsureSubnetSnapshotAsync(profile, metadata, subnetCidr).ConfigureAwait(false);
            if (!IsCurrentScanSession(scanSessionId, tunnelName))
            {
                return;
            }

            currentMetadata = metadata;
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (IsCurrentScanSession(scanSessionId, tunnelName))
                {
                    ReplaceHosts(metadata.Hosts);
                }
            });

            var progress = new Progress<NeighborScanProgress>(update =>
            {
                if (!IsCurrentScanSession(scanSessionId, tunnelName))
                {
                    return;
                }

                PhaseDisplay = update.Phase switch
                {
                    NeighborScanPhase.Ping => $"Ping 扫描中 {update.ProcessedHosts}/{update.TotalHosts}",
                    NeighborScanPhase.Ports => $"端口扫描中 {update.ProcessedHosts}/{update.TotalHosts}",
                    NeighborScanPhase.Hostnames => $"主机名解析中 {update.ProcessedHosts}/{update.TotalHosts}",
                    NeighborScanPhase.Completed => $"扫描完成 · 在线 {update.AliveHosts}",
                    _ => "待扫描"
                };
            });
            var hostProgress = new Progress<NeighborHostUpdate>(update =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!IsCurrentScanSession(scanSessionId, tunnelName))
                    {
                        return;
                    }

                    UpsertHost(update.Host, metadataPath);
                    EmptyStateText = string.Empty;
                    RaiseSummaryPropertiesChanged();
                });
            });

            var result = await scanner.ScanAsync(
                profile,
                metadata,
                progress,
                hostProgress,
                cancellationToken: scanCts.Token).ConfigureAwait(false);

            if (!IsCurrentScanSession(scanSessionId, tunnelName))
            {
                return;
            }

            var mergedMetadata = NetworkNeighborScanner.MergeScanIntoMetadata(metadata, result);
            await metadataStore.SaveAsync(mergedMetadata, scanCts.Token).ConfigureAwait(false);
            if (!IsCurrentScanSession(scanSessionId, tunnelName))
            {
                return;
            }

            currentMetadata = mergedMetadata;
            LastScanDisplay = result.CompletedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            SubnetDisplay = result.SubnetCidr;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (IsCurrentScanSession(scanSessionId, tunnelName))
                {
                    ReplaceHosts(mergedMetadata.Hosts);
                    RaiseSummaryPropertiesChanged();
                }
            });

            logService.WriteInfo($"Neighbor scan completed for '{tunnelName}' with {result.Hosts.Count(host => host.IsAlive)} alive host(s).");
        }
        catch (OperationCanceledException)
        {
            if (IsCurrentTunnel(tunnelName))
            {
                PhaseDisplay = "扫描已取消";
            }

            logService.WriteInfo($"Neighbor scan canceled for '{tunnelName}'.");
        }
        catch (Exception exception)
        {
            if (IsCurrentScanSession(scanSessionId, tunnelName))
            {
                PhaseDisplay = "扫描失败";
            }

            logService.WriteError(exception.Message);
        }
        finally
        {
            EndScanSession(scanSessionId, scanCts, tunnelName);
        }
    }

    private Task CancelScanAsync()
    {
        CancelActiveScan();
        return Task.CompletedTask;
    }

    private Task ToggleScanAsync()
    {
        return IsScanning ? CancelScanAsync() : ScanAsync();
    }

    private Task CopySubnetRemarksAsync()
    {
        try
        {
            var remarks = Hosts
                .Where(host => !string.IsNullOrWhiteSpace(host.Remark))
                .OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer())
                .ToDictionary(host => host.IpAddress, host => host.Remark.Trim(), StringComparer.OrdinalIgnoreCase);

            if (remarks.Count == 0)
            {
                throw new InvalidOperationException("当前网段没有可复制的备注名。");
            }

            var json = JsonSerializer.Serialize(remarks, RemarkJsonOptions);
            systemInteractionService.CopyText(json);
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.Message, "复制网段备注名");
        }

        return Task.CompletedTask;
    }

    private async Task ImportRemarksFromClipboardAsync()
    {
        try
        {
            if (currentProfile is null)
            {
                throw new InvalidOperationException("请先选择隧道。");
            }

            var importedRemarks = ReadRemarkMapFromClipboard();
            if (importedRemarks.Count == 0)
            {
                throw new InvalidOperationException("剪切板 JSON 中没有可导入的备注名。");
            }

            var visibleHosts = await Application.Current.Dispatcher.InvokeAsync(() => Hosts.Select(host => host.ToModel()).ToList());
            var changed = false;
            foreach (var host in visibleHosts)
            {
                if (!importedRemarks.TryGetValue(host.IpAddress, out var importedRemark))
                {
                    continue;
                }

                var normalizedRemark = string.IsNullOrWhiteSpace(importedRemark) ? null : importedRemark.Trim();
                if (string.Equals(host.Remark, normalizedRemark, StringComparison.Ordinal))
                {
                    continue;
                }

                host.Remark = normalizedRemark;
                host.RemarkSource = normalizedRemark is null ? NeighborRemarkSource.None : NeighborRemarkSource.Manual;
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            currentMetadata ??= await metadataStore.GetAsync(currentProfile.Name).ConfigureAwait(false);
            currentMetadata.TunnelName = currentProfile.Name;
            currentMetadata.SubnetCidr = HasScannableSubnet ? SubnetDisplay : currentMetadata.SubnetCidr;
            currentMetadata.Hosts.Clear();
            currentMetadata.Hosts.AddRange(visibleHosts);
            await metadataStore.SaveAsync(currentMetadata).ConfigureAwait(false);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ReplaceHosts(currentMetadata.Hosts);
                RaiseSummaryPropertiesChanged();
            });
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.Message, "从剪切板读取备注名");
        }
    }

    private Dictionary<string, string?> ReadRemarkMapFromClipboard()
    {
        var text = systemInteractionService.GetClipboardText();
        var imported = JsonSerializer.Deserialize<Dictionary<string, string?>>(text, RemarkJsonOptions)
            ?? throw new InvalidOperationException("剪切板 JSON 不是有效的 IP 到备注名对象。");

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ipAddress, remark) in imported)
        {
            if (!IPAddress.TryParse(ipAddress, out _))
            {
                throw new InvalidOperationException($"备注名 JSON 包含无效 IP: {ipAddress}");
            }

            result[ipAddress] = remark;
        }

        return result;
    }

    private async Task RunAutomaticScanAsync()
    {
        if (!CanScan())
        {
            return;
        }

        await ScanAsync().ConfigureAwait(false);
    }

    private static bool ShouldRunAutomaticScan(NeighborMetadata metadata)
    {
        return !metadata.LastScanCompletedAt.HasValue;
    }

    private void CancelActiveScan()
    {
        CancellationTokenSource? cancellationTokenSource;
        lock (scanSessionLock)
        {
            scanSessionVersion++;
            cancellationTokenSource = scanCancellationTokenSource;
            scanCancellationTokenSource = null;
        }

        cancellationTokenSource?.Cancel();
        IsScanning = false;
    }

    private void UpsertHost(NeighborHost host)
    {
        UpsertHost(host, metadataFilePath);
    }

    private void UpsertHost(NeighborHost host, string sourceMetadataFilePath)
    {
        if (hostIndex.TryGetValue(host.IpAddress, out var existingHost))
        {
            existingHost.UpdateFromHost(host);
            return;
        }

        var item = new NeighborHostItemViewModel(host, sourceMetadataFilePath, OnRemarkChanged, messageService, systemInteractionService, textInputDialogService, interconnectService, fileDialogService);
        hostIndex[host.IpAddress] = item;
        Hosts.Add(item);
        SortHosts();
    }

    private void ReplaceHosts(IEnumerable<NeighborHost> hosts)
    {
        Hosts.Clear();
        hostIndex.Clear();
        foreach (var host in hosts.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()))
        {
            var item = new NeighborHostItemViewModel(host, metadataFilePath, OnRemarkChanged, messageService, systemInteractionService, textInputDialogService, interconnectService, fileDialogService);
            hostIndex[host.IpAddress] = item;
            Hosts.Add(item);
        }

        EmptyStateText = Hosts.Count == 0
            ? (HasScannableSubnet ? (isConnected ? "尚未发现主机。" : "隧道未连接，连接后自动扫描。") : "当前隧道没有可扫描的 IPv4 网段。")
            : string.Empty;
    }

    private void ClearVisibleHosts()
    {
        Hosts.Clear();
        hostIndex.Clear();
    }

    private async void OnRemarkChanged(NeighborHostItemViewModel host)
    {
        try
        {
            if (currentProfile is null)
            {
                return;
            }

            currentMetadata ??= await metadataStore.GetAsync(currentProfile.Name).ConfigureAwait(false);
            currentMetadata.TunnelName = currentProfile.Name;
            currentMetadata.SubnetCidr = HasScannableSubnet ? SubnetDisplay : currentMetadata.SubnetCidr;

            var existing = currentMetadata.Hosts.FirstOrDefault(item => string.Equals(item.IpAddress, host.IpAddress, StringComparison.OrdinalIgnoreCase));
            var updatedModel = host.ToModel();
            if (existing is null)
            {
                currentMetadata.Hosts.Add(updatedModel);
            }
            else
            {
                currentMetadata.Hosts.Remove(existing);
                currentMetadata.Hosts.Add(updatedModel);
            }

            await metadataStore.SaveAsync(currentMetadata).ConfigureAwait(false);
            RaiseSummaryPropertiesChanged();
        }
        catch (Exception exception)
        {
            logService.WriteError($"保存邻居备注失败: {exception.Message}");
        }
    }

    private void SortHosts()
    {
        var sorted = Hosts.OrderBy(host => IPAddress.Parse(host.IpAddress), new IpAddressComparer()).ToList();
        Hosts.Clear();
        foreach (var host in sorted)
        {
            Hosts.Add(host);
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

    private void RaiseSummaryPropertiesChanged()
    {
        RaisePropertyChanged(nameof(OnlineCount));
        RaisePropertyChanged(nameof(OfflineCount));
        RaisePropertyChanged(nameof(UnassignedCount));
        RaisePropertyChanged(nameof(HostnameResolvedCount));
        RaisePropertyChanged(nameof(StatusSummary));
    }

    private void NotifyCommandStateChanged()
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelScanCommand.NotifyCanExecuteChanged();
    }

    private async Task<NeighborMetadata> EnsureSubnetSnapshotAsync(TunnelProfile profile, NeighborMetadata metadata, string subnetCidr)
    {
        var addresses = subnetCalculator.EnumerateHostAddresses(subnetCidr);
        var existingHosts = metadata.Hosts.ToDictionary(host => host.IpAddress, StringComparer.OrdinalIgnoreCase);
        var snapshot = new NeighborMetadata
        {
            TunnelName = profile.Name,
            SubnetCidr = subnetCidr,
            LastScanStartedAt = metadata.LastScanStartedAt,
            LastScanCompletedAt = metadata.LastScanCompletedAt
        };

        foreach (var address in addresses)
        {
            if (existingHosts.TryGetValue(address, out var existingHost))
            {
                snapshot.Hosts.Add(new NeighborHost
                {
                    IpAddress = existingHost.IpAddress,
                    Remark = existingHost.Remark,
                    RemarkSource = existingHost.RemarkSource,
                    Hostname = existingHost.Hostname,
                    IsAlive = existingHost.IsAlive,
                    PingMs = existingHost.PingMs,
                    IsRdpOpen = existingHost.IsRdpOpen,
                    IsSshOpen = existingHost.IsSshOpen,
                    IsInterconnectOpen = existingHost.IsInterconnectOpen,
                    LastSeenAt = existingHost.LastSeenAt,
                    LastScannedAt = existingHost.LastScannedAt
                });
                continue;
            }

            snapshot.Hosts.Add(new NeighborHost { IpAddress = address });
        }

        if (HasMetadataChanged(metadata, snapshot))
        {
            await metadataStore.SaveAsync(snapshot).ConfigureAwait(false);
        }

        return snapshot;
    }

    private long BeginScanSession(CancellationTokenSource cancellationTokenSource)
    {
        lock (scanSessionLock)
        {
            scanCancellationTokenSource?.Cancel();
            scanSessionVersion++;
            scanCancellationTokenSource = cancellationTokenSource;
            return scanSessionVersion;
        }
    }

    private void EndScanSession(long scanSessionId, CancellationTokenSource cancellationTokenSource, string tunnelName)
    {
        var isCurrentSession = false;
        lock (scanSessionLock)
        {
            if (scanSessionVersion == scanSessionId && ReferenceEquals(scanCancellationTokenSource, cancellationTokenSource))
            {
                scanCancellationTokenSource = null;
                isCurrentSession = true;
            }
        }

        if (isCurrentSession && IsCurrentTunnel(tunnelName))
        {
            IsScanning = false;
        }
    }

    private bool IsCurrentScanSession(long scanSessionId, string tunnelName)
    {
        lock (scanSessionLock)
        {
            return scanSessionVersion == scanSessionId && IsCurrentTunnel(tunnelName);
        }
    }

    private bool IsCurrentTunnel(string tunnelName)
    {
        return currentProfile is not null && string.Equals(currentProfile.Name, tunnelName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMetadataChanged(NeighborMetadata left, NeighborMetadata right)
    {
        if (!string.Equals(left.SubnetCidr, right.SubnetCidr, StringComparison.OrdinalIgnoreCase) ||
            left.Hosts.Count != right.Hosts.Count)
        {
            return true;
        }

        for (var index = 0; index < left.Hosts.Count; index++)
        {
            if (!string.Equals(left.Hosts[index].IpAddress, right.Hosts[index].IpAddress, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
