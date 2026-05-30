namespace MyWireGuard.App.Services;

public interface ITextInputDialogService
{
    bool TryShow(string title, string prompt, string initialValue, out string value);

    bool TryShowMultiline(string title, string prompt, string initialValue, out string value);
}
