using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Data.EntityFramework.Connections;

/// <summary>
/// Configures a module's context. The module names the connection it needs; the host
/// chooses the provider.
/// </summary>
public interface IDbContextOptionsConfigurator
{
    void Configure(DbContextOptionsBuilder options, string connectionName);
}

public static class DbContextRegistration
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string connectionName)
        where TContext : DbContext
        => services.AddDbContext<TContext>((provider, options) =>
            provider.GetRequiredService<IDbContextOptionsConfigurator>().Configure(options, connectionName));
}
