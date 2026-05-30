namespace MyWireGuard.Core.Models;

public static class InterconnectLimits
{
    public const int DefaultPort = 7727;
    public const long MaxFileSizeBytes = 500L * 1024 * 1024;
}

public sealed record InterconnectReceiveTextRecord(
    DateTimeOffset ReceivedAt,
    string SourceIpAddress,
    string Text);

public sealed record InterconnectReceiveFileRecord(
    DateTimeOffset ReceivedAt,
    string SourceIpAddress,
    string FileName,
    long FileSize,
    string SavedPath);

public sealed record InterconnectSendProgress(
    string TargetIpAddress,
    string FileName,
    long BytesTransferred,
    long TotalBytes);

public sealed record InterconnectHostCapability(bool IsInterconnectAvailable);
