using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Interop;
using MyWireGuard.Infrastructure.Runtime;

namespace MyWireGuard.Infrastructure.Services;

public sealed class TunnelDllKeypairService : IKeypairService
{
    private readonly RuntimeAssetLocator runtimeAssetLocator;

    public TunnelDllKeypairService(RuntimeAssetLocator runtimeAssetLocator)
    {
        this.runtimeAssetLocator = runtimeAssetLocator;
    }

    public GeneratedKeypair Generate()
    {
        var runtimeResult = runtimeAssetLocator.EnsureRuntimeAvailable();
        if (!runtimeResult.IsAvailable)
        {
            throw new FileNotFoundException(runtimeAssetLocator.BuildMissingRuntimeMessage());
        }

        var (publicKey, privateKey) = TunnelDllNative.GenerateKeypair();
        return new GeneratedKeypair(Convert.ToBase64String(publicKey), Convert.ToBase64String(privateKey));
    }
}