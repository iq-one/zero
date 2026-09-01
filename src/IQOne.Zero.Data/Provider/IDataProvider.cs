using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Data.Provider;

/// <summary>
/// Bundles every service bound to a specific data provider, so switching providers
/// is a single registration change.
/// </summary>
public interface IDataProvider
{
    string Name { get; }

    void Register(IServiceCollection services);
}
