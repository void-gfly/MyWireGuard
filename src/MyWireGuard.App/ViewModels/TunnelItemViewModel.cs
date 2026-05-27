using MyWireGuard.Core.Models;

namespace MyWireGuard.App.ViewModels;

public sealed class TunnelItemViewModel : ObservableObject
{
    private TunnelStatus status;

    public TunnelItemViewModel(TunnelProfile profile, TunnelStatus status)
    {
        Profile = profile;
        this.status = status;
    }

    public TunnelProfile Profile { get; }

    public string Name => Profile.Name;

    public string ConfigPath => Profile.ConfigPath ?? string.Empty;

    public TunnelStatus Status
    {
        get => status;
        set
        {
            if (SetProperty(ref status, value))
            {
                RaisePropertyChanged(nameof(StatusDisplay));
            }
        }
    }

    public string StatusDisplay => Status switch
    {
        TunnelStatus.Started => "已连接",
        TunnelStatus.Starting => "启动中",
        TunnelStatus.Stopping => "停止中",
        TunnelStatus.Stopped => "已停止",
        _ => "未知"
    };

    public string AddressDisplay => Profile.Interface.Addresses.Count > 0
        ? string.Join(", ", Profile.Interface.Addresses)
        : "(未配置地址)";

    public int PeerCount => Profile.Peers.Count;

    public string DnsDisplay => Profile.Interface.DnsServers.Count > 0
        ? string.Join(", ", Profile.Interface.DnsServers)
        : "(无)";

    public string EndpointDisplay => Profile.Peers.Count > 0 && Profile.Peers[0].Endpoint is not null
        ? Profile.Peers[0].Endpoint!
        : "(无)";
}