using System.Windows;
using System.Windows.Threading;
using System.IO;
using MyWireGuard.App.Dialogs;
using MyWireGuard.App.Services;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.App.ViewModels;

public sealed class NeighborHostItemViewModel : ObservableObject
{
    private readonly Action<NeighborHostItemViewModel> onRemarkChanged;
    private readonly IMessageService messageService;
    private readonly ISystemInteractionService systemInteractionService;
    private readonly ITextInputDialogService textInputDialogService;
    private readonly IInterconnectService interconnectService;
    private readonly IFileDialogService fileDialogService;
    private readonly string metadataFilePath;
    private string ipAddress;
    private string remark;
    private NeighborRemarkSource remarkSource;
    private string hostname;
    private bool isAlive;
    private int? pingMs;
    private bool isRdpOpen;
    private bool isSshOpen;
    private bool isInterconnectOpen;
    private DateTimeOffset? lastSeenAt;
    private DateTimeOffset? lastScannedAt;
    private bool suppressRemarkNotification;

    public NeighborHostItemViewModel(
        NeighborHost host,
        string metadataFilePath,
        Action<NeighborHostItemViewModel> onRemarkChanged,
        IMessageService messageService,
        ISystemInteractionService systemInteractionService,
        ITextInputDialogService textInputDialogService,
        IInterconnectService interconnectService,
        IFileDialogService fileDialogService)
    {
        this.metadataFilePath = metadataFilePath;
        this.onRemarkChanged = onRemarkChanged;
        this.messageService = messageService;
        this.systemInteractionService = systemInteractionService;
        this.textInputDialogService = textInputDialogService;
        this.interconnectService = interconnectService;
        this.fileDialogService = fileDialogService;
        ipAddress = host.IpAddress;
        remark = host.Remark ?? string.Empty;
        remarkSource = host.RemarkSource;
        hostname = host.Hostname ?? string.Empty;
        isAlive = host.IsAlive;
        pingMs = host.PingMs;
        isRdpOpen = host.IsRdpOpen;
        isSshOpen = host.IsSshOpen;
        isInterconnectOpen = host.IsInterconnectOpen;
        lastSeenAt = host.LastSeenAt;
        lastScannedAt = host.LastScannedAt;

        OpenRemoteDesktopCommand = new AsyncRelayCommand(OpenRemoteDesktopAsync, () => IsRdpOpen);
        OpenSshCommand = new AsyncRelayCommand(OpenSshAsync, () => IsSshOpen);
        CopyNameCommand = new AsyncRelayCommand(CopyNameAsync);
        CopyIpCommand = new AsyncRelayCommand(CopyIpAsync);
        CopyFileCommand = new AsyncRelayCommand(CopyFileAsync, CanUseMetadataFile);
        OpenFileCommand = new AsyncRelayCommand(OpenFileAsync, CanUseMetadataFile);
        OpenContainingFolderCommand = new AsyncRelayCommand(OpenContainingFolderAsync, CanUseMetadataFile);
        RenameCommand = new AsyncRelayCommand(RenameAsync);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => !string.IsNullOrWhiteSpace(Remark));
        SendTextCommand = new AsyncRelayCommand(SendTextAsync, () => HasInterconnectActions);
        SendFileCommand = new AsyncRelayCommand(SendFileAsync, () => HasInterconnectActions);
    }

    public AsyncRelayCommand OpenRemoteDesktopCommand { get; }

    public AsyncRelayCommand OpenSshCommand { get; }

    public AsyncRelayCommand CopyNameCommand { get; }

    public AsyncRelayCommand CopyIpCommand { get; }

    public AsyncRelayCommand CopyFileCommand { get; }

    public AsyncRelayCommand OpenFileCommand { get; }

    public AsyncRelayCommand OpenContainingFolderCommand { get; }

    public AsyncRelayCommand RenameCommand { get; }

    public AsyncRelayCommand DeleteCommand { get; }

    public AsyncRelayCommand SendTextCommand { get; }

    public AsyncRelayCommand SendFileCommand { get; }

    public string IpAddress
    {
        get => ipAddress;
        private set
        {
            if (SetProperty(ref ipAddress, value))
            {
                RaisePropertyChanged(nameof(IpAddressLastSegmentSortKey));
            }
        }
    }

    public int IpAddressLastSegmentSortKey
    {
        get
        {
            var segments = IpAddress.Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 4 && int.TryParse(segments[3], out var lastSegment))
            {
                return lastSegment;
            }

            return int.MaxValue;
        }
    }

    public string Remark
    {
        get => remark;
        set
        {
            if (!SetProperty(ref remark, value))
            {
                return;
            }

            RemarkSource = string.IsNullOrWhiteSpace(value)
                ? NeighborRemarkSource.None
                : NeighborRemarkSource.Manual;

            RaisePropertyChanged(nameof(DisplayName));
            RaisePropertyChanged(nameof(StatusDisplay));
            DeleteCommand.NotifyCanExecuteChanged();

            if (!suppressRemarkNotification)
            {
                onRemarkChanged(this);
            }
        }
    }

    public NeighborRemarkSource RemarkSource
    {
        get => remarkSource;
        private set => SetProperty(ref remarkSource, value);
    }

    public string Hostname
    {
        get => hostname;
        private set
        {
            if (SetProperty(ref hostname, value))
            {
                RaisePropertyChanged(nameof(StatusDisplay));
                RaisePropertyChanged(nameof(HostnameDisplay));
            }
        }
    }

    public bool IsAlive
    {
        get => isAlive;
        private set
        {
            if (SetProperty(ref isAlive, value))
            {
                RaisePropertyChanged(nameof(StatusDisplay));
                RaisePropertyChanged(nameof(StatusBadgeText));
                RaisePropertyChanged(nameof(StatusBadgeKind));
            }
        }
    }

    public int? PingMs
    {
        get => pingMs;
        private set
        {
            if (SetProperty(ref pingMs, value))
            {
                RaisePropertyChanged(nameof(PingDisplay));
                RaisePropertyChanged(nameof(PingBadgeText));
                RaisePropertyChanged(nameof(PingBadgeKind));
            }
        }
    }

    public bool IsRdpOpen
    {
        get => isRdpOpen;
        private set
        {
            if (SetProperty(ref isRdpOpen, value))
            {
                RaisePropertyChanged(nameof(RdpDisplay));
                RaisePropertyChanged(nameof(HasOpenPorts));
                OpenRemoteDesktopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsSshOpen
    {
        get => isSshOpen;
        private set
        {
            if (SetProperty(ref isSshOpen, value))
            {
                RaisePropertyChanged(nameof(SshDisplay));
                RaisePropertyChanged(nameof(HasOpenPorts));
                OpenSshCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsInterconnectOpen
    {
        get => isInterconnectOpen;
        private set
        {
            if (SetProperty(ref isInterconnectOpen, value))
            {
                RaisePropertyChanged(nameof(HasInterconnectActions));
                SendTextCommand.NotifyCanExecuteChanged();
                SendFileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTimeOffset? LastSeenAt
    {
        get => lastSeenAt;
        private set
        {
            if (SetProperty(ref lastSeenAt, value))
            {
                RaisePropertyChanged(nameof(LastSeenDisplay));
            }
        }
    }

    public DateTimeOffset? LastScannedAt
    {
        get => lastScannedAt;
        private set
        {
            if (SetProperty(ref lastScannedAt, value))
            {
                RaisePropertyChanged(nameof(HasBeenScanned));
                RaisePropertyChanged(nameof(PingBadgeText));
                RaisePropertyChanged(nameof(PingBadgeKind));
                RaisePropertyChanged(nameof(StatusBadgeText));
                RaisePropertyChanged(nameof(StatusBadgeKind));
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Remark) ? "未分配" : Remark;

    public string HostnameDisplay => string.IsNullOrWhiteSpace(Hostname) ? "-" : Hostname;

    public string PingDisplay => PingMs.HasValue ? $"{PingMs.Value} ms" : "-";

    public bool HasBeenScanned => LastScannedAt.HasValue;

    public string PingBadgeText => PingMs.HasValue
        ? $"{PingMs.Value} ms"
        : HasBeenScanned
            ? "超时"
            : "读取中...";

    public string PingBadgeKind => PingMs.HasValue
        ? "success"
        : HasBeenScanned
            ? "danger"
            : "neutral";

    public string RdpDisplay => IsRdpOpen ? "3389" : "-";

    public string SshDisplay => IsSshOpen ? "22" : "-";

    public bool HasOpenPorts => IsRdpOpen || IsSshOpen;

    public bool HasInterconnectActions => IsInterconnectOpen;

    public string LastSeenDisplay => LastSeenAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "-";

    public string StatusDisplay => IsAlive ? "在线" : "离线";

    public string StatusBadgeText => IsAlive
        ? "正常"
        : HasBeenScanned
            ? "超时"
            : "待检测";

    public string StatusBadgeKind => IsAlive
        ? "success"
        : HasBeenScanned
            ? "warning"
            : "neutral";

    private string PreferredName => !string.IsNullOrWhiteSpace(Remark)
        ? Remark.Trim()
        : !string.IsNullOrWhiteSpace(Hostname)
            ? Hostname.Trim()
            : IpAddress;

    public NeighborHost ToModel()
    {
        return new NeighborHost
        {
            IpAddress = IpAddress,
            Remark = string.IsNullOrWhiteSpace(Remark) ? null : Remark.Trim(),
            RemarkSource = RemarkSource,
            Hostname = string.IsNullOrWhiteSpace(Hostname) ? null : Hostname,
            IsAlive = IsAlive,
            PingMs = PingMs,
            IsRdpOpen = IsRdpOpen,
            IsSshOpen = IsSshOpen,
            IsInterconnectOpen = IsInterconnectOpen,
            LastSeenAt = LastSeenAt,
            LastScannedAt = LastScannedAt
        };
    }

    public void UpdateFromHost(NeighborHost host)
    {
        suppressRemarkNotification = true;
        try
        {
            IpAddress = host.IpAddress;
            if (string.IsNullOrWhiteSpace(Remark))
            {
                Remark = host.Remark ?? string.Empty;
                RemarkSource = host.RemarkSource;
            }

            Hostname = host.Hostname ?? string.Empty;
            IsAlive = host.IsAlive;
            PingMs = host.PingMs;
            IsRdpOpen = host.IsRdpOpen;
            IsSshOpen = host.IsSshOpen;
            IsInterconnectOpen = host.IsInterconnectOpen;
            LastSeenAt = host.LastSeenAt;
            LastScannedAt = host.LastScannedAt;
            RaisePropertyChanged(nameof(DisplayName));
        }
        finally
        {
            suppressRemarkNotification = false;
        }
    }

    private bool CanUseMetadataFile()
    {
        return !string.IsNullOrWhiteSpace(metadataFilePath);
    }

    private Task OpenRemoteDesktopAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.OpenRemoteDesktop(IpAddress), "远程桌面");
    }

    private Task OpenSshAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.OpenSsh(IpAddress), "SSH");
    }

    private Task CopyNameAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.CopyText(PreferredName), "复制名字");
    }

    private Task CopyIpAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.CopyText(IpAddress), "复制 IP");
    }

    private Task CopyFileAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.CopyFile(metadataFilePath), "复制文件");
    }

    private Task OpenFileAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.OpenFile(metadataFilePath), "打开文件");
    }

    private Task OpenContainingFolderAsync()
    {
        return ExecuteSystemActionAsync(() => systemInteractionService.OpenContainingFolder(metadataFilePath), "打开所在目录");
    }

    private Task RenameAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            dispatcher.BeginInvoke(new Action(ShowRenameDialog), DispatcherPriority.ApplicationIdle);
            return Task.CompletedTask;
        }

        ShowRenameDialog();
        return Task.CompletedTask;
    }

    private Task DeleteAsync()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            dispatcher.BeginInvoke(new Action(ShowDeleteConfirmation), DispatcherPriority.ApplicationIdle);
            return Task.CompletedTask;
        }

        ShowDeleteConfirmation();
        return Task.CompletedTask;
    }

    private async Task SendTextAsync()
    {
        try
        {
            if (!textInputDialogService.TryShow("发送文本信息", $"发送到 {IpAddress}", string.Empty, out var text))
            {
                return;
            }

            var trimmed = text.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return;
            }

            await interconnectService.SendTextAsync(IpAddress, InterconnectLimits.DefaultPort, trimmed, CancellationToken.None).ConfigureAwait(false);
            messageService.ShowInfo("文本发送完成。", "互联");
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.Message, "发送文本失败");
        }
    }

    private async Task SendFileAsync()
    {
        try
        {
            var filePath = fileDialogService.PickSendFilePath();
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return;
            }

            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                throw new FileNotFoundException("发送文件不存在。", filePath);
            }

            if (fileInfo.Length > InterconnectLimits.MaxFileSizeBytes)
            {
                throw new InvalidOperationException("发送文件不能超过 500MB。");
            }

            var progressWindow = CreateProgressWindow(fileInfo.Name, fileInfo.Length);
            var progress = new Progress<InterconnectSendProgress>(update =>
            {
                progressWindow.UpdateProgress(update.BytesTransferred, update.TotalBytes);
            });

            progressWindow.Show();
            try
            {
                await interconnectService.SendFileAsync(IpAddress, InterconnectLimits.DefaultPort, filePath, progress, CancellationToken.None).ConfigureAwait(false);
                progressWindow.Close();
                messageService.ShowInfo("文件发送完成。", "互联");
            }
            catch
            {
                progressWindow.Close();
                throw;
            }
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.Message, "发送文件失败");
        }
    }

    private FileSendProgressDialog CreateProgressWindow(string fileName, long totalBytes)
    {
        var owner = Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
            ?? Application.Current?.MainWindow;
        return new FileSendProgressDialog(owner, IpAddress, fileName, totalBytes);
    }

    private void ShowRenameDialog()
    {
        try
        {
            if (!textInputDialogService.TryShow("改名", $"为 {IpAddress} 设置备注名", Remark, out var updatedRemark))
            {
                return;
            }

            Remark = updatedRemark.Trim();
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.ToString(), "改名失败");
        }
    }

    private void ShowDeleteConfirmation()
    {
        try
        {
            if (!messageService.Confirm($"确定要清空 {IpAddress} 的备注名吗？", "删除备注"))
            {
                return;
            }

            Remark = string.Empty;
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.ToString(), "删除备注失败");
        }
    }

    private Task ExecuteSystemActionAsync(Action action, string title)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            messageService.ShowError(exception.Message, title);
        }

        return Task.CompletedTask;
    }
}
