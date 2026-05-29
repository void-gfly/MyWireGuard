using System.Windows;
using MyWireGuard.App;
using MyWireGuard.App.Services;

namespace MyWireGuard.Tests;

public sealed class GuiSingleInstanceTests
{
    [Fact]
    public void DetermineStartupMode_ShouldBypassSingleInstanceForServiceHost()
    {
        var mode = AppStartupDecider.DetermineStartupMode(["/service", "wg0.conf"], isPrimaryInstance: false);

        Assert.Equal(AppStartupMode.RunTunnelServiceHost, mode);
    }

    [Fact]
    public void DetermineStartupMode_ShouldNotifyExistingGui_WhenSecondaryGuiInstanceStarts()
    {
        var mode = AppStartupDecider.DetermineStartupMode([], isPrimaryInstance: false);

        Assert.Equal(AppStartupMode.NotifyExistingGui, mode);
    }

    [Theory]
    [InlineData("show-main-window", true)]
    [InlineData("SHOW-MAIN-WINDOW", true)]
    [InlineData("show-main-window ", false)]
    [InlineData(" show-main-window", false)]
    [InlineData("show-main-window-now", false)]
    [InlineData("", false)]
    public void IsSupportedCommand_ShouldOnlyAcceptExpectedCommand(string command, bool expected)
    {
        Assert.Equal(expected, SingleInstanceCoordinator.IsSupportedCommand(command));
    }

    [Theory]
    [InlineData(false, false, WindowState.Minimized)]
    [InlineData(true, true, WindowState.Minimized)]
    [InlineData(true, true, WindowState.Normal)]
    public void ComputeActivationState_ShouldRestoreVisibleNormalWindow(bool isVisible, bool showInTaskbar, WindowState windowState)
    {
        var state = MainWindow.ComputeActivationState(isVisible, showInTaskbar, windowState);

        Assert.True(state.ShouldShowWindow);
        Assert.True(state.ShouldShowInTaskbar);
        Assert.Equal(WindowState.Normal, state.TargetWindowState);
        Assert.True(state.ShouldActivate);
        Assert.True(state.ShouldTemporarilySetTopmost);
    }
}
