namespace MyWireGuard.Infrastructure.Services;

public static class TunnelServiceRecoveryPolicy
{
    public const int ResetPeriodSeconds = 24 * 60 * 60;
    public const int RestartDelayMilliseconds = 5000;

    public static TunnelServiceRecoveryAction[] CreateActions()
    {
        return
        [
            new(TunnelServiceRecoveryActionType.Restart, RestartDelayMilliseconds),
            new(TunnelServiceRecoveryActionType.Restart, RestartDelayMilliseconds),
            new(TunnelServiceRecoveryActionType.Restart, RestartDelayMilliseconds)
        ];
    }
}

public readonly record struct TunnelServiceRecoveryAction(
    TunnelServiceRecoveryActionType Type,
    int DelayMilliseconds);

public enum TunnelServiceRecoveryActionType
{
    None = 0,
    Restart = 1
}
