using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class TunnelServiceRecoveryPolicyTests
{
    [Fact]
    public void CreateActions_ShouldRestartThreeTimesWithFiveSecondDelay()
    {
        var actions = TunnelServiceRecoveryPolicy.CreateActions();

        Assert.Equal(3, actions.Length);
        Assert.All(actions, action =>
        {
            Assert.Equal(TunnelServiceRecoveryActionType.Restart, action.Type);
            Assert.Equal(5000, action.DelayMilliseconds);
        });
    }

    [Fact]
    public void ResetPeriodSeconds_ShouldResetFailureCountAfterOneDay()
    {
        Assert.Equal(86400, TunnelServiceRecoveryPolicy.ResetPeriodSeconds);
    }
}
