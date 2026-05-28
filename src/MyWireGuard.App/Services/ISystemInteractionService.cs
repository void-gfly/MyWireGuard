namespace MyWireGuard.App.Services;

public interface ISystemInteractionService
{
    void CopyText(string text);

    void CopyFile(string path);

    void OpenRemoteDesktop(string ipAddress);

    void OpenSsh(string ipAddress);

    void OpenFile(string path);

    void OpenContainingFolder(string path);
}