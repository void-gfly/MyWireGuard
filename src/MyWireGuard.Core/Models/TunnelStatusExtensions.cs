namespace MyWireGuard.Core.Models;

public static class TunnelStatusExtensions
{
    public static bool RequiresServiceStatusRefresh(this TunnelStatus status)
    {
        return status is TunnelStatus.Started or TunnelStatus.Starting or TunnelStatus.Stopping;
    }

    public static bool IsUnexpectedStopFrom(this TunnelStatus status, TunnelStatus previousStatus)
    {
        return previousStatus == TunnelStatus.Started && status == TunnelStatus.Stopped;
    }
}
