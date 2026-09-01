using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Persistence;

/// <summary>Adds the transaction boundary around commands.</summary>
/// <remarks>
/// This package defines contracts only. A provider package —
/// <c>IQOne.Zero.Persistence.EntityFramework</c>, for instance — supplies the
/// implementations and has its own <c>Add</c> call.
/// </remarks>
public static class PersistenceRegistration
{
    /// <summary>
    /// Wraps every command in a transaction, committing when it succeeds and rolling back
    /// when it does not.
    /// </summary>
    /// <remarks>
    /// Queries are left alone. Call this after the provider's own registration, which is
    /// what supplies <see cref="IUnitOfWork"/>.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroTransactions(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        return services;
    }
}
