using MyWireGuard.Core.Models;

namespace MyWireGuard.Tests;

public sealed class TunnelStatusRefreshPolicyTests
{
    [Theory]
    [InlineData(TunnelStatus.Started)]
    [InlineData(TunnelStatus.Starting)]
    [InlineData(TunnelStatus.Stopping)]
    public void RequiresServiceStatusRefresh_ShouldTrackActiveAndTransitioningStatuses(TunnelStatus status)
    {
        Assert.True(status.RequiresServiceStatusRefresh());
    }

    [Theory]
    [InlineData(TunnelStatus.Stopped)]
    [InlineData(TunnelStatus.Unknown)]
    public void RequiresServiceStatusRefresh_ShouldIgnoreInactiveStatuses(TunnelStatus status)
    {
        Assert.False(status.RequiresServiceStatusRefresh());
    }

    [Fact]
    public void IsUnexpectedStopFrom_ShouldDetectStartedToStoppedTransition()
    {
        Assert.True(TunnelStatus.Stopped.IsUnexpectedStopFrom(TunnelStatus.Started));
    }

    [Theory]
    [InlineData(TunnelStatus.Stopped, TunnelStatus.Stopping)]
    [InlineData(TunnelStatus.Stopped, TunnelStatus.Starting)]
    [InlineData(TunnelStatus.Unknown, TunnelStatus.Started)]
    [InlineData(TunnelStatus.Started, TunnelStatus.Started)]
    public void IsUnexpectedStopFrom_ShouldIgnoreOtherTransitions(TunnelStatus currentStatus, TunnelStatus previousStatus)
    {
        Assert.False(currentStatus.IsUnexpectedStopFrom(previousStatus));
    }
}
