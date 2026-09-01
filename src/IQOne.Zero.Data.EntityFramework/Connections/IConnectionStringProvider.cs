using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Data.EntityFramework.Connections;

/// <summary>
/// Resolves connection strings.
/// </summary>
/// <remarks>
/// Values come from environment variables in deployed environments and user secrets in
/// development. A password-bearing value read from a configuration file outside
/// development fails startup.
/// </remarks>
public interface IConnectionStringProvider : ISingleton
{
    string Get(string name);
}
