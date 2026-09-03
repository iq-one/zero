using IQOne.Zero.Persistence.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Persistence.EntityFramework;

/// <summary>Records which context the open-generic repositories resolve through.</summary>
/// <param name="Context">The bound context type.</param>
internal sealed record ZeroBoundContext(Type Context);

/// <summary>Adds Entity Framework Core as the implementation behind Zero's data access.</summary>
public static class EntityFrameworkRegistration
{
    /// <summary>
    /// Registers the context, the unit of work, a repository for every aggregate, the
    /// specification evaluator and the save-changes interceptors.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call is the whole wiring. What is left to the application is the engine — this
    /// package names none — and its own conventions:
    /// </para>
    /// <code>
    /// services.AddZeroEntityFramework&lt;ShopContext&gt;(options => options.UseNpgsql(connectionString));
    /// services.AddSingleton&lt;IEntityFilterConvention, SoftDeleted&gt;();
    /// services.AddZeroTransactions();
    /// </code>
    /// <para>
    /// Every registration is a <c>TryAdd</c>, so an application that wants its own evaluator,
    /// its own unit of work or a hand-written repository for one aggregate registers it
    /// before this call and keeps it.
    /// </para>
    /// </remarks>
    /// <typeparam name="TContext">The application's context.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">
    /// How the context connects. Optional only because a context may configure itself in
    /// <c>OnConfiguring</c>; most applications pass the provider and connection string here.
    /// </param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroEntityFramework<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder>? configure = null)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        // Stateless: it reads a specification and builds an expression. One instance is enough.
        services.TryAddSingleton(SpecificationEvaluator.Default);

        // Scoped, because a convention usually depends on the request — who is signed in,
        // which tenant, what the clock says at the moment of the write.
        services.TryAddScoped<SaveChangesConventionInterceptor>();
        services.TryAddScoped<WriteOwnershipInterceptor>();

        services.AddDbContext<TContext>((provider, options) =>
        {
            // Order matters. A convention may turn a delete into a soft-delete update, and the
            // ownership check has to see the operation that will actually reach the table.
            options.AddInterceptors(
                provider.GetRequiredService<SaveChangesConventionInterceptor>(),
                provider.GetRequiredService<WriteOwnershipInterceptor>());

            configure?.Invoke(options);
        });

        // The repositories and the unit of work are open over any context: they take the base
        // type so that one open-generic registration serves every aggregate in the model.
        //
        // That is also why a second context cannot simply be added. TryAdd would make the
        // second call a no-op, leaving every repository bound to the FIRST context — and the
        // symptom is not an error but a query against the wrong database. Refusing is the
        // only safe answer, and the message says what to do instead.
        var bound = services.FirstOrDefault(d => d.ServiceType == typeof(ZeroBoundContext));

        if (bound?.ImplementationInstance is ZeroBoundContext existing && existing.Context != typeof(TContext))
            throw new InvalidOperationException(
                $"AddZeroEntityFramework is already bound to '{existing.Context.Name}' and cannot also " +
                $"bind '{typeof(TContext).Name}'. The open-generic IRepository<T> resolves through a " +
                $"single DbContext, so a second binding would silently read from the first context's " +
                $"database. For an application with a context per module, register that module's " +
                $"repositories explicitly against its own context: " +
                $"'public sealed class OrderRepository(OrderContext context, ISpecificationEvaluator evaluator) " +
                $": EfRepository<Order>(context, evaluator), IOrderRepository;'. " +
                $"The unit of work is named the same way: " +
                $"'public sealed class OrderWork(OrderContext context) : EfUnitOfWork(context), IOrderWork;' " +
                $"-- and such an application opens its transaction in the handler, because " +
                $"AddZeroTransactions registers one behaviour over every request and cannot pick a " +
                $"context. Call AddZeroEntityFramework once, for the context the open generics should " +
                $"serve, and use AddDbContext for the others.");

        services.TryAddSingleton(new ZeroBoundContext(typeof(TContext)));

        services.TryAddScoped<DbContext>(provider => provider.GetRequiredService<TContext>());
        services.TryAddScoped<IUnitOfWork, EfUnitOfWork>();

        services.TryAdd(ServiceDescriptor.Scoped(typeof(IReadRepository<>), typeof(EfRepository<>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<>), typeof(EfRepository<>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IReadRepository<,>), typeof(EfRepository<,>)));
        services.TryAdd(ServiceDescriptor.Scoped(typeof(IRepository<,>), typeof(EfRepository<,>)));

        return services;
    }
}
