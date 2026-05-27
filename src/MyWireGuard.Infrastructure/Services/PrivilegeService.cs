using System.Security.Principal;
using MyWireGuard.Core.Abstractions;

namespace MyWireGuard.Infrastructure.Services;

public sealed class PrivilegeService : IPrivilegeService
{
    public bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}