using System.Windows;
using MyWireGuard.App.Dialogs;

namespace MyWireGuard.App.Services;

public sealed class TextInputDialogService : ITextInputDialogService
{
    public bool TryShow(string title, string prompt, string initialValue, out string value)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue, isMultiline: false);
        return ShowDialog(dialog, initialValue, out value);
    }

    public bool TryShowMultiline(string title, string prompt, string initialValue, out string value)
    {
        var dialog = new TextInputDialog(title, prompt, initialValue, isMultiline: true);
        return ShowDialog(dialog, initialValue, out value);
    }

    private static bool ShowDialog(TextInputDialog dialog, string initialValue, out string value)
    {
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
