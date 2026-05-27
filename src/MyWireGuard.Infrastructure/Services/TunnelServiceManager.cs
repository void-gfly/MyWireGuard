using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MyWireGuard.Core.Abstractions;
using MyWireGuard.Core.Models;
using MyWireGuard.Infrastructure.Runtime;

namespace MyWireGuard.Infrastructure.Services;

public sealed class TunnelServiceManager : ITunnelServiceManager
{
    private const string ServiceDescriptionText = "WireGuard tunnel hosted by MyWireGuard.";
    private const string ServiceDependencies = "Nsi\0TcpIp\0";
    private readonly ILogService logService;
    private readonly RuntimeAssetLocator runtimeAssetLocator;

    public TunnelServiceManager(ILogService logService, RuntimeAssetLocator runtimeAssetLocator)
    {
        this.logService = logService;
        this.runtimeAssetLocator = runtimeAssetLocator;
    }

    public Task<TunnelStatus> GetStatusAsync(string tunnelName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = QueryStatus(tunnelName);
        return Task.FromResult(status);
    }

    public Task EnsureServiceConfigurationAsync(TunnelProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(profile.ConfigPath))
        {
            return Task.CompletedTask;
        }

        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.ScmAccessRights.AllAccess);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeMethods.OpenService(scm, GetServiceName(profile.Name), NativeMethods.ServiceAccessRights.AllAccess);
            if (service == IntPtr.Zero)
            {
                return Task.CompletedTask;
            }

            try
            {
                var pathAndArgs = BuildServiceCommandLine(profile.ConfigPath);
                ApplyServiceConfiguration(service, $"MyWireGuard: {profile.Name}", pathAndArgs);
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(TunnelProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureRuntimeAvailable();
        AddOrStart(profile.ConfigPath ?? throw new InvalidOperationException("Config path is missing."));
        logService.WriteInfo($"Start requested for tunnel '{profile.Name}'.");
        return Task.CompletedTask;
    }

    public Task StopAsync(string tunnelName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ControlAndWaitForStop(tunnelName, waitForStop: true);
        logService.WriteInfo($"Stop requested for tunnel '{tunnelName}'.");
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string tunnelName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RemoveService(tunnelName, waitForStop: true);
        logService.WriteInfo($"Removed service for tunnel '{tunnelName}'.");
        return Task.CompletedTask;
    }

    public string GetServiceName(string tunnelName)
    {
        return $"WireGuardTunnel${tunnelName}";
    }

    public bool IsRuntimeAvailable()
    {
        return runtimeAssetLocator.EnsureRuntimeAvailable().IsAvailable;
    }

    private void EnsureRuntimeAvailable()
    {
        var runtimeResult = runtimeAssetLocator.EnsureRuntimeAvailable();
        if (!runtimeResult.IsAvailable)
        {
            throw new FileNotFoundException(runtimeAssetLocator.BuildMissingRuntimeMessage());
        }
    }

    private TunnelStatus QueryStatus(string tunnelName)
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.ScmAccessRights.Connect);
        if (scm == IntPtr.Zero)
        {
            return TunnelStatus.Unknown;
        }

        try
        {
            var service = NativeMethods.OpenService(scm, GetServiceName(tunnelName), NativeMethods.ServiceAccessRights.QueryStatus);
            if (service == IntPtr.Zero)
            {
                return TunnelStatus.Stopped;
            }

            try
            {
                var serviceStatus = new NativeMethods.ServiceStatus();
                if (!NativeMethods.QueryServiceStatus(service, ref serviceStatus))
                {
                    return TunnelStatus.Unknown;
                }

                return serviceStatus.dwCurrentState switch
                {
                    NativeMethods.ServiceState.StartPending => TunnelStatus.Starting,
                    NativeMethods.ServiceState.Running => TunnelStatus.Started,
                    NativeMethods.ServiceState.StopPending => TunnelStatus.Stopping,
                    NativeMethods.ServiceState.Stopped => TunnelStatus.Stopped,
                    _ => TunnelStatus.Unknown
                };
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    private void AddOrStart(string configPath)
    {
        var tunnelName = Path.GetFileNameWithoutExtension(configPath);
        var shortName = GetServiceName(tunnelName);
        var displayName = $"MyWireGuard: {tunnelName}";
        var pathAndArgs = BuildServiceCommandLine(configPath);

        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.ScmAccessRights.AllAccess);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeMethods.OpenService(scm, shortName, NativeMethods.ServiceAccessRights.AllAccess);
            if (service != IntPtr.Zero)
            {
                try
                {
                    ApplyServiceConfiguration(service, displayName, pathAndArgs);
                    if (!NativeMethods.StartService(service, 0, null) && Marshal.GetLastWin32Error() != 1056)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    return;
                }
                finally
                {
                    NativeMethods.CloseServiceHandle(service);
                }
            }

            service = NativeMethods.CreateService(
                scm,
                shortName,
                displayName,
                NativeMethods.ServiceAccessRights.AllAccess,
                NativeMethods.ServiceType.Win32OwnProcess,
                NativeMethods.ServiceStartType.AutoStart,
                NativeMethods.ServiceError.Normal,
                pathAndArgs,
                null,
                IntPtr.Zero,
                ServiceDependencies,
                null,
                null);

            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                ApplyServiceConfiguration(service, displayName, pathAndArgs);

                if (!NativeMethods.StartService(service, 0, null))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    private static string ResolveServiceHostExecutablePath()
    {
        var serviceHostPath = Path.Combine(AppContext.BaseDirectory, "MyWireGuard.ServiceHost.exe");
        if (File.Exists(serviceHostPath))
        {
            return serviceHostPath;
        }

        throw new FileNotFoundException("MyWireGuard.ServiceHost.exe was not found in the application directory.", serviceHostPath);
    }

    private static string BuildServiceCommandLine(string configPath)
    {
        var serviceHostPath = ResolveServiceHostExecutablePath();
        return $"\"{serviceHostPath}\" /service \"{configPath}\" {Process.GetCurrentProcess().Id}";
    }

    private static void ApplyServiceConfiguration(IntPtr service, string displayName, string pathAndArgs)
    {
        if (!NativeMethods.ChangeServiceConfig(
                service,
                NativeMethods.ServiceType.Win32OwnProcess,
                NativeMethods.ServiceStartType.AutoStart,
                NativeMethods.ServiceError.Normal,
                pathAndArgs,
                null,
                IntPtr.Zero,
                ServiceDependencies,
                null,
                null,
                displayName))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var sidType = NativeMethods.ServiceSidType.Unrestricted;
        if (!NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigType.SidInfo, ref sidType))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        var description = new NativeMethods.ServiceDescription
        {
            lpDescription = ServiceDescriptionText
        };

        if (!NativeMethods.ChangeServiceConfig2(service, NativeMethods.ServiceConfigType.Description, ref description))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private void ControlAndWaitForStop(string tunnelName, bool waitForStop)
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.ScmAccessRights.AllAccess);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeMethods.OpenService(scm, GetServiceName(tunnelName), NativeMethods.ServiceAccessRights.AllAccess);
            if (service == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var status = new NativeMethods.ServiceStatus();
                NativeMethods.ControlService(service, NativeMethods.ServiceControl.Stop, ref status);

                for (var i = 0; waitForStop && i < 30; i++)
                {
                    if (!NativeMethods.QueryServiceStatus(service, ref status) || status.dwCurrentState == NativeMethods.ServiceState.Stopped)
                    {
                        break;
                    }

                    Thread.Sleep(1000);
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    private void RemoveService(string tunnelName, bool waitForStop)
    {
        var scm = NativeMethods.OpenSCManager(null, null, NativeMethods.ScmAccessRights.AllAccess);
        if (scm == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var service = NativeMethods.OpenService(scm, GetServiceName(tunnelName), NativeMethods.ServiceAccessRights.AllAccess);
            if (service == IntPtr.Zero)
            {
                return;
            }

            try
            {
                var status = new NativeMethods.ServiceStatus();
                NativeMethods.ControlService(service, NativeMethods.ServiceControl.Stop, ref status);

                for (var i = 0; waitForStop && i < 30; i++)
                {
                    if (!NativeMethods.QueryServiceStatus(service, ref status) || status.dwCurrentState == NativeMethods.ServiceState.Stopped)
                    {
                        break;
                    }

                    Thread.Sleep(1000);
                }

                if (!NativeMethods.DeleteService(service))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 1072)
                    {
                        throw new Win32Exception(error);
                    }
                }
            }
            finally
            {
                NativeMethods.CloseServiceHandle(service);
            }
        }
        finally
        {
            NativeMethods.CloseServiceHandle(scm);
        }
    }

    private static class NativeMethods
    {
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenSCManager(string? machineName, string? databaseName, ScmAccessRights dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr OpenService(IntPtr hScManager, string lpServiceName, ServiceAccessRights dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern IntPtr CreateService(
            IntPtr hScManager,
            string lpServiceName,
            string lpDisplayName,
            ServiceAccessRights dwDesiredAccess,
            ServiceType dwServiceType,
            ServiceStartType dwStartType,
            ServiceError dwErrorControl,
            string lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ChangeServiceConfig(
            IntPtr hService,
            ServiceType dwServiceType,
            ServiceStartType dwStartType,
            ServiceError dwErrorControl,
            string lpBinaryPathName,
            string? lpLoadOrderGroup,
            IntPtr lpdwTagId,
            string? lpDependencies,
            string? lpServiceStartName,
            string? lpPassword,
            string? lpDisplayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool StartService(IntPtr hService, int dwNumServiceArgs, string? lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool DeleteService(IntPtr hService);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool ControlService(IntPtr hService, ServiceControl dwControl, ref ServiceStatus lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool QueryServiceStatus(IntPtr hService, ref ServiceStatus dwServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern bool ChangeServiceConfig2(IntPtr hService, ServiceConfigType dwInfoLevel, ref ServiceSidType lpInfo);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool ChangeServiceConfig2(IntPtr hService, ServiceConfigType dwInfoLevel, ref ServiceDescription lpInfo);

        [Flags]
        public enum ScmAccessRights : uint
        {
            Connect = 0x0001,
            CreateService = 0x0002,
            EnumerateService = 0x0004,
            Lock = 0x0008,
            QueryLockStatus = 0x0010,
            ModifyBootConfig = 0x0020,
            StandardRightsRequired = 0xF0000,
            AllAccess = StandardRightsRequired | Connect | CreateService | EnumerateService | Lock | QueryLockStatus | ModifyBootConfig
        }

        [Flags]
        public enum ServiceAccessRights : uint
        {
            QueryConfig = 0x0001,
            ChangeConfig = 0x0002,
            QueryStatus = 0x0004,
            EnumerateDependents = 0x0008,
            Start = 0x0010,
            Stop = 0x0020,
            PauseContinue = 0x0040,
            Interrogate = 0x0080,
            UserDefinedControl = 0x0100,
            Delete = 0x00010000,
            ReadControl = 0x00020000,
            WriteDac = 0x00040000,
            WriteOwner = 0x00080000,
            AllAccess = 0xF01FF
        }

        public enum ServiceType : uint
        {
            Win32OwnProcess = 0x00000010
        }

        public enum ServiceStartType : uint
        {
            AutoStart = 0x00000002,
            Demand = 0x00000003
        }

        public enum ServiceError : uint
        {
            Normal = 0x00000001
        }

        public enum ServiceControl : uint
        {
            Stop = 0x00000001
        }

        public enum ServiceState : uint
        {
            Stopped = 0x00000001,
            StartPending = 0x00000002,
            StopPending = 0x00000003,
            Running = 0x00000004
        }

        public enum ServiceConfigType : uint
        {
            Description = 1,
            SidInfo = 5
        }

        public enum ServiceSidType : uint
        {
            Unrestricted = 0x00000001
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct ServiceStatus
        {
            public uint dwServiceType;
            public ServiceState dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct ServiceDescription
        {
            public string lpDescription;
        }
    }
}