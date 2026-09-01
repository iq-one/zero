using IQOne.Zero.Persistence.Conventions;
using IQOne.Zero.Persistence.EntityFramework.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Persistence.EntityFramework.Tests;

/// <summary>
/// The promise the capability contract makes: install the package, write one line, done.
/// </summary>
public sealed class RegistrationTests : IDisposable
{
    private readonly ShopDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private ServiceProvider Provider(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();

        services.AddZeroEntityFramework<PlainShopContext>(options => options.UseSqlite(_database.Connection));

        extra?.Invoke(services);

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    [Fact]
    public void The_entry_point_alone_registers_everything_a_consumer_touches()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ISpecificationEvaluator>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork>();
        scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRepository<Invoice>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IReadRepository<Invoice, int>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IRepository<Invoice, int>>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<DbContext>().Should().BeOfType<PlainShopContext>();
    }

    [Fact]
    public void The_unit_of_work_and_the_repositories_share_one_context()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Otherwise a handler could add through a repository and save through a unit of work
        // that never saw the change.
        scope.ServiceProvider.GetRequiredService<PlainShopContext>().Should().BeSameAs(context);
    }

    [Fact]
    public async Task A_repository_resolved_from_the_container_reads_and_writes()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        var repository = scope.ServiceProvider.GetRequiredService<IRepository<Invoice, int>>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repository.AddAsync(new Invoice { Tenant = "north", Customer = "Gear", Total = 100 });

        await using (var transaction = await unitOfWork.BeginTransactionAsync())
        {
            await unitOfWork.SaveChangesAsync();
            await transaction.CompleteAsync();
        }

        (await repository.CountAsync(new EveryInvoice())).Should().Be(1);
    }

    [Fact]
    public async Task Conventions_registered_by_the_application_are_wired_into_the_context()
    {
        using var provider = Provider(services =>
        {
            services.AddSingleton<ISaveChangesConvention<DbContext>, StampTenantOnWrite>();
            services.AddSingleton<IWriteOwnership, InvoicesOnly>();
        });

        using var scope = provider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<PlainShopContext>();
        context.Tenant = "south";

        context.Invoices.Add(new Invoice { Customer = "Gear", Total = 100 });
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        await using var after = _database.Plain();
        (await after.Invoices.SingleAsync()).Tenant.Should().Be("south",
            "the save-changes interceptor is registered by the entry point, not by the consumer");

        context.Ledger.Add(new LedgerEntry { Note = "posted" });

        var saving = () => scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        await saving.Should().ThrowAsync<WriteOwnershipViolationException>(
            "so is the ownership guard");
    }

    [Fact]
    public void An_application_that_registers_its_own_evaluator_first_keeps_it()
    {
        var mine = new SpecificationEvaluator();

        var services = new ServiceCollection();
        services.AddSingleton<ISpecificationEvaluator>(mine);
        services.AddZeroEntityFramework<PlainShopContext>(options => options.UseSqlite(_database.Connection));

        using var provider = services.BuildServiceProvider();

        // TryAdd throughout, so nothing the application chose is quietly overwritten.
        provider.GetRequiredService<ISpecificationEvaluator>().Should().BeSameAs(mine);
    }

    [Fact]
    public void The_interceptors_are_resolvable_on_their_own()
    {
        using var provider = Provider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<SaveChangesConventionInterceptor>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<WriteOwnershipInterceptor>().Should().NotBeNull();
    }
}
