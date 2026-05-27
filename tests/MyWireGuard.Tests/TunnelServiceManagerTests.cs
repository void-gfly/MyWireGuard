using System.Reflection;
using MyWireGuard.Infrastructure.Services;

namespace MyWireGuard.Tests;

public sealed class TunnelServiceManagerTests
{
    [Fact]
    public void BuildServiceCommandLine_UsesStandaloneServiceHostWithoutParentProcessId()
    {
        var serviceHostPath = Path.Combine(AppContext.BaseDirectory, "MyWireGuard.ServiceHost.exe");
        File.WriteAllText(serviceHostPath, string.Empty);

        try
        {
            var method = typeof(TunnelServiceManager).GetMethod(
                "BuildServiceCommandLine",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var configPath = Path.Combine(Path.GetTempPath(), "wg-demo.conf");
            var commandLine = Assert.IsType<string>(method.Invoke(null, [configPath]));

            Assert.Equal($"\"{serviceHostPath}\" /service \"{configPath}\"", commandLine);
        }
        finally
        {
            File.Delete(serviceHostPath);
        }
    }
}
