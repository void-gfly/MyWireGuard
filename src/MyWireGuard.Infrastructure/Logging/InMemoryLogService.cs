using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;

namespace MyWireGuard.Infrastructure.Logging;

public sealed class InMemoryLogService : ILogService
{
    private readonly object syncRoot = new();
    private readonly List<LogEntry> entries = [];

    public event EventHandler<LogEntry>? EntryWritten;

    public IReadOnlyList<LogEntry> GetEntries()
    {
        lock (syncRoot)
        {
            return entries.ToArray();
        }
    }

    public void WriteInfo(string message)
    {
        Write("Info", message);
    }

    public void WriteError(string message)
    {
        Write("Error", message);
    }

    private void Write(string level, string message)
    {
        var entry = new LogEntry(DateTimeOffset.Now, level, message);
        lock (syncRoot)
        {
            entries.Add(entry);
            if (entries.Count > 500)
            {
                entries.RemoveAt(0);
            }
        }

        EntryWritten?.Invoke(this, entry);
    }
}