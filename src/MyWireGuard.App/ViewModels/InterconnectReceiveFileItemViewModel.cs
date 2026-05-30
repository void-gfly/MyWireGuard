using MyWireGuard.App.Services;
using System.IO;
using MyWireGuard.Core.Models;

namespace MyWireGuard.App.ViewModels;

public sealed class InterconnectReceiveFileItemViewModel
{
    private readonly ISystemInteractionService systemInteractionService;

    public InterconnectReceiveFileItemViewModel(InterconnectReceiveFileRecord record, ISystemInteractionService systemInteractionService)
    {
        this.systemInteractionService = systemInteractionService;
        ReceivedAt = record.ReceivedAt;
        SourceIpAddress = record.SourceIpAddress;
        FileName = record.FileName;
        FileSize = record.FileSize;
        SavedPath = record.SavedPath;
        CopyPathCommand = new AsyncRelayCommand(CopyPathAsync);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
    }

    public DateTimeOffset ReceivedAt { get; }

    public string SourceIpAddress { get; }

    public string FileName { get; }

    public long FileSize { get; }

    public string FileSizeDisplay => FileSize >= 1024 * 1024
        ? $"{FileSize / 1024d / 1024d:F2} MB"
        : $"{FileSize / 1024d:F1} KB";

    public string SavedPath { get; }

    public AsyncRelayCommand CopyPathCommand { get; }

    public AsyncRelayCommand OpenFolderCommand { get; }

    private Task CopyPathAsync()
    {
        systemInteractionService.CopyText(SavedPath);
        return Task.CompletedTask;
    }

    private Task OpenFolderAsync()
    {
        var directoryPath = Path.GetDirectoryName(SavedPath)
            ?? throw new InvalidOperationException("接收文件目录不存在。");
        systemInteractionService.OpenFolder(directoryPath);
        return Task.CompletedTask;
    }
}
