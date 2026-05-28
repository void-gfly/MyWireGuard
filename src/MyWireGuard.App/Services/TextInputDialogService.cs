using System.Windows;
using MyWireGuard.App.Dialogs;

namespace MyWireGuard.App.Services;

public sealed class TextInputDialogService : ITextInputDialogService
{
    public bool TryShow(string title, string prompt, string initialValue, out string value)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue);
        var mainWindow = Application.Current?.MainWindow;
        if (mainWindow is not null && mainWindow.IsVisible)
        {
            dialog.Owner = mainWindow;
        }

        var accepted = dialog.ShowDialog() == true;
        value = accepted ? dialog.ResultText : initialValue;
        return accepted;
    }
}