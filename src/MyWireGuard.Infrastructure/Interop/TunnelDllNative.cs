using System.Runtime.InteropServices;

namespace MyWireGuard.Infrastructure.Interop;

public static class TunnelDllNative
{
    [DllImport("tunnel.dll", EntryPoint = "WireGuardTunnelService", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WireGuardTunnelService(string configFile);

    [DllImport("tunnel.dll", EntryPoint = "WireGuardGenerateKeypair", CallingConvention = CallingConvention.Cdecl)]
    private static extern void WireGuardGenerateKeypair(byte[] publicKey, byte[] privateKey);

    public static bool RunTunnelService(string configFile)
    {
        return WireGuardTunnelService(configFile);
    }

    public static (byte[] PublicKey, byte[] PrivateKey) GenerateKeypair()
    {
        var publicKey = new byte[32];
        var privateKey = new byte[32];
        WireGuardGenerateKeypair(publicKey, privateKey);
        return (publicKey, privateKey);
    }
}