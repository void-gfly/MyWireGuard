namespace MyWireGuard.Core.Abstractions;

public interface IPrivilegeService
{
    bool IsElevated { get; }
}