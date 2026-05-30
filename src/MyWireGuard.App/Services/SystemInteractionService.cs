using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MyWireGuard.App.Services;

public sealed class SystemInteractionService : ISystemInteractionService
{
    public void CopyText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("没有可复制的内容。");
        }

        Clipboard.SetText(text.Trim());
    }

    public void CopyFile(string path)
    {
        EnsureFileExists(path);

        var files = new StringCollection
        {
            path
        };

        Clipboard.SetFileDropList(files);
    }

    public void OpenRemoteDesktop(string ipAddress)
    {
        StartProcess("mstsc.exe", $"/v:{ipAddress}");
    }

    public void OpenSsh(string ipAddress)
    {
        StartProcess("cmd.exe", $"/c start \"\" ssh {ipAddress}");
    }

    public void OpenFile(string path)
    {
        EnsureFileExists(path);

        _ = Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("无法打开文件。") ;
    }

    public void OpenContainingFolder(string path)
    {
        EnsureFileExists(path);
        StartProcess("explorer.exe", $"/select,\"{path}\"");
    }

    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("目标目录路径为空。");
        }

        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"目标目录不存在: {path}");
        }

        StartProcess("explorer.exe", $"\"{path}\"");
    }

    private static void EnsureFileExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("目标文件路径为空。");
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("目标文件不存在。", path);
        }
    }

    private static void StartProcess(string fileName, string arguments)
    {
        _ = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = true
        }) ?? throw new InvalidOperationException($"无法启动 {fileName}。");
    }
}
