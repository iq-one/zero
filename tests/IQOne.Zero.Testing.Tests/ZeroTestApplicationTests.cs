using IQOne.Zero.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// The test application exists so that a wiring mistake fails in a test rather than at
/// startup. These check both halves of that: that a correctly wired application works
/// end to end, and that each mistake startup refuses is refused here too.
/// </summary>
public class ZeroTestApplicationTests
{
    /// <summary>
    /// The capability contract's own test: the package alone is enough. Nothing here adds
    /// messaging, validation, a registry or a sender, and the request still travels the whole
    /// pipeline in a container built the way the application builds its own.
    /// </summary>
    [Fact]
    public async Task The_package_alone_is_enough_to_send_a_request()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddHandler<CloseInvoice, Unit>(new StubHandler<CloseInvoice, Unit>(Unit.Success))
            .BuildAsync();

        (await app.SendAsync(new CloseInvoice(1))).ShouldSucceed();
    }

    [Fact]
    public async Task A_request_reaches_its_handler_through_the_real_sender()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddHandler<CreateInvoice, int>(new CreateInvoiceHandler(new InMemoryInvoiceStore()))
            .BuildAsync();

        var result = await app.SendAsync(new CreateInvoice("INV-001", 250m));

        result.ShouldHaveValue(1);
    }

    [Fact]
    public async Task A_handler_the_container_builds_gets_its_dependencies()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddServices(services => services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>())
            .AddHandler<CreateInvoice, int, CreateInvoiceHandler>()
            .BuildAsync();

        (await app.SendAsync(new CreateInvoice("INV-001", 250m))).ShouldSucceed();
    }

    [Fact]
    public async Task Validation_is_in_the_pipeline_without_being_asked_for()
    {
        var handler = StubHandler<CreateInvoice, int>.Returning(1);

        await using var app = await ZeroTestApplication.Create()
            .AddHandler<CreateInvoice, int>(handler)
            .AddValidator(new CreateInvoiceValidator())
            .BuildAsync();

        var result = await app.SendAsync(new CreateInvoice("", 0m));

        result.ShouldFailWithCodes("invoice.reference", "invoice.amount");
        handler.ShouldNotHaveRun();
    }

    [Fact]
    public async Task A_validator_may_take_a_dependency()
    {
        var handler = StubHandler<CreateInvoice, int>.Returning(1);

        await using var app = await ZeroTestApplication.Create()
            .AddServices(services => services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>())
            .AddHandler<CreateInvoice, int>(handler)
            .AddValidator<CreateInvoice, UniqueReferenceValidator>()
            .BuildAsync();

        (await app.SendAsync(new CreateInvoice("INV-001", 250m))).ShouldSucceed();
    }

    [Fact]
    public async Task A_behaviour_added_for_every_request_wraps_every_request()
    {
        var log = new List<string>();

        await using var app = await ZeroTestApplication.Create()
            .AddServices(services => services.AddSingleton<IList<string>>(log))
            .AddHandler<CreateInvoice, int>(StubHandler<CreateInvoice, int>.Returning(1))
            .AddHandler<CloseInvoice, Unit>(new StubHandler<CloseInvoice, Unit>(Unit.Success))
            .AddBehavior(typeof(EveryRequestBehavior<,>))
            .BuildAsync();

        await app.SendAsync(new CreateInvoice("INV-001", 250m));
        await app.SendAsync(new CloseInvoice(1));

        log.Should().Equal("CreateInvoice", "CloseInvoice");
    }

    [Fact]
    public void A_behaviour_type_that_is_not_a_behaviour_is_refused_where_it_is_added()
    {
        var add = () => ZeroTestApplication.Create().AddBehavior(typeof(CreateInvoiceHandler));

        add.Should().Throw<ArgumentException>().WithMessage("*open generic pipeline behaviour*");
    }

    [Fact]
    public async Task A_module_brings_its_own_handlers_and_registrations()
    {
        var store = new InMemoryInvoiceStore();
        store.Add("INV-001", 250m);

        await using var app = await ZeroTestApplication.Create()
            .AddServices(services => services.AddSingleton<IInvoiceStore>(store))
            .AddModule(new InvoiceModule())
            .BuildAsync();

        var result = await app.SendAsync(new GetInvoice(1));

        result.ShouldHaveValue(invoice => invoice.Total == 250m);
    }

    [Fact]
    public async Task A_request_nobody_handles_stops_the_build_and_names_it()
    {
        var build = async () => await ZeroTestApplication.Create().AddModule(new HalfBuiltModule()).BuildAsync();

        (await build.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(NobodyHandlesThis),
                "the application refuses to start for this, so a test must refuse to build for it");
    }

    [Fact]
    public async Task Two_handlers_for_one_request_are_refused_no_matter_which_side_they_came_from()
    {
        var build = async () => await ZeroTestApplication.Create()
            .AddServices(services => services.AddSingleton<IInvoiceStore>(new InMemoryInvoiceStore()))
            .AddModule(new InvoiceModule())
            .AddHandler<GetInvoice, InvoiceModel>(StubHandler<GetInvoice, InvoiceModel>.Returning(new InvoiceModel(1, 0m)))
            .BuildAsync();

        (await build.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("has two handlers");
    }

    [Fact]
    public async Task A_dependency_nobody_registered_stops_the_build()
    {
        var build = async () => await ZeroTestApplication.Create()
            .AddHandler<CreateInvoice, int, CreateInvoiceHandler>()
            .BuildAsync();

        (await build.Should().ThrowAsync<AggregateException>())
            .Which.Message.Should().Contain(nameof(IInvoiceStore));
    }

    [Fact]
    public async Task A_singleton_holding_a_scoped_service_stops_the_build()
    {
        var build = async () => await ZeroTestApplication.Create()
            .AddServices(services =>
            {
                services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>();
                services.AddSingleton<CaptiveHolder>();
            })
            .AddHandler<CreateInvoice, int>(new CreateInvoiceHandler(new InMemoryInvoiceStore()))
            .BuildAsync();

        (await build.Should().ThrowAsync<AggregateException>())
            .Which.Message.Should().Contain("scoped", "ValidateScopes is what catches a captive dependency");
    }

    [Fact]
    public async Task Resolving_a_scoped_service_from_the_root_is_refused_the_way_it_would_be_in_production()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddHandler<CreateInvoice, int>(new CreateInvoiceHandler(new InMemoryInvoiceStore()))
            .BuildAsync();

        var resolve = () => app.Services.GetRequiredService<ISender>();

        resolve.Should().Throw<InvalidOperationException>().WithMessage("*scope*");
    }

    [Fact]
    public async Task Each_send_gets_its_own_scope()
    {
        var seen = new List<Guid>();

        await using var app = await ZeroTestApplication.Create()
            .AddServices(services =>
            {
                services.AddScoped<ScopeMarker>();
                services.AddSingleton<IList<Guid>>(seen);
            })
            .AddHandler<CloseInvoice, Unit, ScopeMarkingHandler>()
            .BuildAsync();

        await app.SendAsync(new CloseInvoice(1));
        await app.SendAsync(new CloseInvoice(2));

        seen.Should().HaveCount(2).And.OnlyHaveUniqueItems(
            "a scope per send is what a request gets in production");
    }

    [Fact]
    public async Task InScope_resolves_a_scoped_service_for_an_assertion()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddServices(services => services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>())
            .AddHandler<CreateInvoice, int, CreateInvoiceHandler>()
            .BuildAsync();

        app.InScope<IInvoiceStore, InvoiceModel?>(store => store.Find(1)).Should().BeNull();
    }

    [Fact]
    public async Task Settings_reach_the_configuration_the_application_binds_from()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddConfiguration(("Mail:Host", "smtp.example.com"), ("Mail:Port", "587"))
            .AddHandler<CloseInvoice, Unit>(new StubHandler<CloseInvoice, Unit>(Unit.Success))
            .BuildAsync();

        app.InScope<IConfiguration, string?>(configuration => configuration["Mail:Host"])
            .Should().Be("smtp.example.com");
    }

    [Fact]
    public async Task A_configuration_exists_even_when_the_test_supplied_no_settings()
    {
        await using var app = await ZeroTestApplication.Create()
            .AddHandler<CloseInvoice, Unit>(new StubHandler<CloseInvoice, Unit>(Unit.Success))
            .BuildAsync();

        app.InScope<IConfiguration, string?>(configuration => configuration["Mail:Host"])
            .Should().BeNull("a module that binds options must find an IConfiguration to bind from");
    }
}
