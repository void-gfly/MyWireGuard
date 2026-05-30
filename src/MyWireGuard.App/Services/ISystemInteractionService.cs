namespace MyWireGuard.App.Services;

public interface ISystemInteractionService
{
    void CopyText(string text);

    string GetClipboardText();

    void CopyFile(string path);

    void OpenRemoteDesktop(string ipAddress);

    void OpenSsh(string ipAddress);

    void OpenFile(string path);

    void OpenContainingFolder(string path);

    void OpenFolder(string path);
}
