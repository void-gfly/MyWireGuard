namespace MyWireGuard.App.Services;

public enum ExitConfirmationResult
{
    Cancel = 0,
    ExitUiOnly = 1,
    StopTunnelsAndExit = 2
}

public interface IMessageService
{
    void ShowInfo(string message, string title);

    void ShowError(string message, string title);

    bool Confirm(string message, string title);

    ExitConfirmationResult ConfirmExitWithActiveTunnels(string message, string title);
}