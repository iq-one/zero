using Microsoft.EntityFrameworkCore;

namespace IQOne.Zero.Data.EntityFramework.Connections;

/// <summary>SQL Server configuration with transient-fault retries.</summary>
public sealed class SqlServerDbContextOptionsConfigurator(IConnectionStringProvider connections)
    : IDbContextOptionsConfigurator
{
    public void Configure(DbContextOptionsBuilder builder, string connectionName)
        => builder.UseSqlServer(connections.Get(connectionName), sql =>
        {
            sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            sql.CommandTimeout(30);
        });
}
