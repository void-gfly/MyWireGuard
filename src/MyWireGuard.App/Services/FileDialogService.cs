using Microsoft.Win32;

namespace MyWireGuard.App.Services;

public sealed class FileDialogService : IFileDialogService
{
    public string? PickImportPath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 WireGuard 配置",
            Filter = "WireGuard 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickExportPath(string tunnelName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出 WireGuard 配置",
            FileName = tunnelName + ".conf",
            Filter = "WireGuard 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSendFilePath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择要发送的文件",
            Filter = "所有文件 (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
