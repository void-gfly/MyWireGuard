using MyWireGuard.Core.Models;

namespace MyWireGuard.Core.Abstractions;

public interface IKeypairService
{
    GeneratedKeypair Generate();
}