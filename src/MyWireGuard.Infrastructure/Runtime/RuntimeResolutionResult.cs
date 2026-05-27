namespace MyWireGuard.Infrastructure.Runtime;

public sealed record RuntimeResolutionResult(bool IsAvailable, string Message, IReadOnlyList<string> SearchedDirectories);