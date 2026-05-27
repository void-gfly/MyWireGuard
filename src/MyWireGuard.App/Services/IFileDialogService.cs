namespace MyWireGuard.App.Services;

public interface IFileDialogService
{
    string? PickImportPath();

    string? PickExportPath(string tunnelName);
}