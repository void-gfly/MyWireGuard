using System.Text;
using MyWireGuard.App.Services;

namespace MyWireGuard.Tests;

public sealed class CrashDiagnosticsTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), "MyWireGuard.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void FormatReport_ShouldIncludeExceptionAndRuntimeContext()
    {
        var exception = CreateNestedException();

        var report = CrashDiagnostics.FormatReport(
            exception,
            "DispatcherUnhandledException",
            new DateTimeOffset(2026, 6, 2, 12, 30, 15, TimeSpan.Zero));

        Assert.Contains("MyWireGuard crash report", report);
        Assert.Contains("Source: DispatcherUnhandledException", report);
        Assert.Contains("Time: 2026-06-02T12:30:15.0000000+00:00", report);
        Assert.Contains("ProcessPath:", report);
        Assert.Contains("BaseDirectory:", report);
        Assert.Contains("OSVersion:", report);
        Assert.Contains(".NETVersion:", report);
        Assert.Contains("ExceptionType: System.InvalidOperationException", report);
        Assert.Contains("Message: outer failure", report);
        Assert.Contains("System.ApplicationException: inner failure", report);
    }

    [Fact]
    public void WriteCrashLog_ShouldWriteUtf8FileAndReturnPath()
    {
        var report = "异常内容 abc";
        var occurredAt = new DateTimeOffset(2026, 6, 2, 12, 30, 15, 123, TimeSpan.Zero);

        var path = CrashDiagnostics.WriteCrashLog(tempRoot, report, occurredAt);

        Assert.True(File.Exists(path));
        Assert.Equal(Path.Combine(tempRoot, "Logs"), Path.GetDirectoryName(path));
        Assert.Equal("crash-20260602-123015-123.log", Path.GetFileName(path));
        Assert.Equal(report, File.ReadAllText(path, new UTF8Encoding(false, true)));
    }

    [Fact]
    public void WriteCrashLog_ShouldNotOverwriteExistingFileForSameTimestamp()
    {
        var occurredAt = new DateTimeOffset(2026, 6, 2, 12, 30, 15, 123, TimeSpan.Zero);

        var firstPath = CrashDiagnostics.WriteCrashLog(tempRoot, "first", occurredAt);
        var secondPath = CrashDiagnostics.WriteCrashLog(tempRoot, "second", occurredAt);

        Assert.NotEqual(firstPath, secondPath);
        Assert.Equal("first", File.ReadAllText(firstPath, new UTF8Encoding(false, true)));
        Assert.Equal("second", File.ReadAllText(secondPath, new UTF8Encoding(false, true)));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static Exception CreateNestedException()
    {
        try
        {
            throw new ApplicationException("inner failure");
        }
        catch (Exception exception)
        {
            return new InvalidOperationException("outer failure", exception);
        }
    }
}
