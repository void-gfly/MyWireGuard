using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface ILogService
{
    event EventHandler<LogEntry>? EntryWritten;

    IReadOnlyList<LogEntry> GetEntries();

    void WriteInfo(string message);

    void WriteError(string message);
}