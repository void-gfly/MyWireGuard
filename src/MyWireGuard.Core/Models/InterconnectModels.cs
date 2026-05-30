using System.Text.Json;

namespace MyWireGuard.Core.Models;

public static class InterconnectLimits
{
    public const int DefaultPort = 7727;
    public const int MaxTextMessageBytes = 1024 * 1024;
    public const int MaxFileNameBytes = 1024;
    public const long MaxFileSizeBytes = 500L * 1024 * 1024;
}

public static class InterconnectJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
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

public sealed record InterconnectLocalInfo(
    string ComputerName,
    IReadOnlyList<string> LanIpAddresses);
