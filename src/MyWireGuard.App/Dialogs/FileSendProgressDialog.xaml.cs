using System.Windows;

namespace MyWireGuard.App.Dialogs;

public partial class FileSendProgressDialog : Window
{
    private readonly long totalBytes;

    public FileSendProgressDialog(Window? owner, string targetIpAddress, string fileName, long totalBytes)
    {
        this.totalBytes = totalBytes;
        InitializeComponent();
        Owner = owner;
        TargetTextBlock.Text = $"目标主机: {targetIpAddress}";
        FileTextBlock.Text = $"文件: {fileName}";
        UpdateProgress(0, totalBytes);
    }

    public void UpdateProgress(long transferredBytes, long totalBytes)
    {
        var effectiveTotal = totalBytes <= 0 ? this.totalBytes : totalBytes;
        var percentage = effectiveTotal <= 0 ? 0 : Math.Clamp(transferredBytes * 100d / effectiveTotal, 0d, 100d);
        TransferProgressBar.Value = percentage;
        ProgressTextBlock.Text = $"{FormatBytes(transferredBytes)} / {FormatBytes(effectiveTotal)} ({percentage:F1}%)";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / 1024d / 1024d / 1024d:F2} GB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / 1024d / 1024d:F2} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024d:F1} KB";
        }

        return $"{bytes} B";
    }
}
