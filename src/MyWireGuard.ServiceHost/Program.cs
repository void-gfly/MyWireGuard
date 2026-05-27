using MyWireGuard.Infrastructure.Config;
using MyWireGuard.Infrastructure.Interop;
using MyWireGuard.Infrastructure.Runtime;

if (args.Length < 2 || !string.Equals(args[0], "/service", StringComparison.OrdinalIgnoreCase))
{
    return;
}

var runtimePaths = new AppRuntimePaths();
runtimePaths.EnsureCreated();
var runtimeAssetLocator = new RuntimeAssetLocator(runtimePaths);
var runtimeResult = runtimeAssetLocator.EnsureRuntimeAvailable();
if (!runtimeResult.IsAvailable)
{
    Environment.ExitCode = 1;
    return;
}

Environment.ExitCode = TunnelDllNative.RunTunnelService(args[1]) ? 0 : 1;