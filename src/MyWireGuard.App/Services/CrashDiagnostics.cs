using System.IO;
using System.Reflection;
using System.Text;

namespace MyWireGuard.App.Services;

internal static class CrashDiagnostics
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

    public static string FormatReport(Exception exception, string source, DateTimeOffset occurredAt)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";

        var builder = new StringBuilder();
        builder.AppendLine("MyWireGuard crash report");
        builder.AppendLine($"Time: {occurredAt:O}");
        builder.AppendLine($"Source: {source}");
        builder.AppendLine($"Version: {version}");
        builder.AppendLine($"ProcessPath: {Environment.ProcessPath ?? "unknown"}");
        builder.AppendLine($"BaseDirectory: {AppContext.BaseDirectory}");
        builder.AppendLine($"OSVersion: {Environment.OSVersion}");
        builder.AppendLine($".NETVersion: {Environment.Version}");
        builder.AppendLine($"ProcessId: {Environment.ProcessId}");
        builder.AppendLine($"ThreadId: {Environment.CurrentManagedThreadId}");
        builder.AppendLine($"ExceptionType: {exception.GetType().FullName}");
        builder.AppendLine($"Message: {exception.Message}");
        builder.AppendLine();
        builder.AppendLine(exception.ToString());
        return builder.ToString();
    }

    public static string WriteCrashLog(string appBaseDirectory, string report, DateTimeOffset occurredAt)
    {
        var logDirectory = Path.Combine(appBaseDirectory, "Logs");
        Directory.CreateDirectory(logDirectory);

        var fileNamePrefix = $"crash-{occurredAt:yyyyMMdd-HHmmss-fff}";
        var path = Path.Combine(logDirectory, $"{fileNamePrefix}.log");
        for (var index = 1; File.Exists(path); index++)
        {
            path = Path.Combine(logDirectory, $"{fileNamePrefix}-{index}.log");
        }

        File.WriteAllText(path, report, Utf8NoBom);
        return path;
    }
}
