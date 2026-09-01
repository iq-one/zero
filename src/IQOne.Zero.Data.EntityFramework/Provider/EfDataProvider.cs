using IQOne.Zero.Data.Provider;
using IQOne.Zero.Data.Query;
using IQOne.Zero.Data.EntityFramework.Connections;
using IQOne.Zero.Data.EntityFramework.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Data.EntityFramework.Provider;

/// <summary>Entity Framework provider bundle.</summary>
public sealed class EfDataProvider : IDataProvider
{
    public string Name => "Ef";

    public void Register(IServiceCollection services)
    {
        services.TryAddScoped<IQueryExecutor, EfQueryExecutor>();
        services.TryAddScoped(typeof(ITextSearch<>), typeof(EfTextSearch<>));
        services.TryAddSingleton<IDbContextOptionsConfigurator, SqlServerDbContextOptionsConfigurator>();
    }
}

public static class DataProviderRegistration
{
    /// <exception cref="InvalidOperationException">No provider matches <paramref name="providerName"/>.</exception>
    public static IServiceCollection AddDataProvider(
        this IServiceCollection services, string providerName, params IDataProvider[] available)
    {
        IDataProvider[] providers = available.Length > 0 ? available : [new EfDataProvider()];

        var provider = providers.FirstOrDefault(p =>
            string.Equals(p.Name, providerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"'{providerName}' veri saglayicisi tanimli degil. Mevcut: {string.Join(", ", providers.Select(p => p.Name))}");

        provider.Register(services);
        services.AddSingleton(provider);

        return services;
    }
}
