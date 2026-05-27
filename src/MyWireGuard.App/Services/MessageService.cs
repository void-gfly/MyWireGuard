using System.Windows;

namespace MyWireGuard.App.Services;

public sealed class MessageService : IMessageService
{
    public void ShowInfo(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void ShowError(string message, string title)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public bool Confirm(string message, string title)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    public ExitConfirmationResult ConfirmExitWithActiveTunnels(string message, string title)
    {
        return MessageBox.Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Warning) switch
        {
            MessageBoxResult.Yes => ExitConfirmationResult.ExitUiOnly,
            MessageBoxResult.No => ExitConfirmationResult.StopTunnelsAndExit,
            _ => ExitConfirmationResult.Cancel
        };
    }
}