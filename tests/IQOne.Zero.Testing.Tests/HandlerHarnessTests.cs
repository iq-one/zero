using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// The harness replaces the registry, the entry, the pipeline lambda and the provider that a
/// test of one handler otherwise builds by hand. These check that what it builds instead is
/// the real pipeline and not an approximation of it.
/// </summary>
public class HandlerHarnessTests
{
    private static readonly Error Refused = Error.Forbidden("invoice.forbidden", "Not your invoice.");

    [Fact]
    public async Task A_handler_runs_with_the_dependencies_the_test_gave_it()
    {
        var result = await HandlerHarness
            .For<CreateInvoice, int>(new CreateInvoiceHandler(new InMemoryInvoiceStore()))
            .SendAsync(new CreateInvoice("INV-001", 250m));

        result.ShouldHaveValue(1);
    }

    [Fact]
    public async Task A_handler_can_be_built_from_the_harness_container()
    {
        var result = await HandlerHarness
            .For<CreateInvoice, int>(services => new CreateInvoiceHandler(services.GetRequiredService<IInvoiceStore>()))
            .WithServices(services => services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>())
            .SendAsync(new CreateInvoice("INV-001", 250m));

        result.ShouldSucceed().Should().Be(1);
    }

    [Fact]
    public async Task Behaviours_wrap_the_handler_lowest_order_outermost()
    {
        var log = new List<string>();

        await HandlerHarness
            .For<CreateInvoice, int>(StubHandler<CreateInvoice, int>.Returning(1))
            // Registered inner-first on purpose: only Order may decide the nesting.
            .WithBehavior(new RecordingBehavior<CreateInvoice, int>(log, "inner", BehaviorOrder.Transaction))
            .WithBehavior(new RecordingBehavior<CreateInvoice, int>(log, "outer", BehaviorOrder.Logging))
            .SendAsync(new CreateInvoice("INV-001", 250m));

        log.Should().Equal("outer:in", "inner:in", "inner:out", "outer:out");
    }

    [Fact]
    public async Task A_behaviour_may_stop_the_request_before_the_handler()
    {
        var handler = StubHandler<CreateInvoice, int>.Returning(1);

        var result = await HandlerHarness
            .For<CreateInvoice, int>(handler)
            .WithBehavior(new ShortCircuitBehavior<CreateInvoice, int>(Refused))
            .SendAsync(new CreateInvoice("INV-001", 250m));

        result.ShouldFailWith("invoice.forbidden");
        handler.ShouldNotHaveRun();
    }

    [Fact]
    public async Task A_validator_runs_in_the_pipeline_and_reports_every_failure_at_once()
    {
        var handler = StubHandler<CreateInvoice, int>.Returning(1);

        var result = await HandlerHarness
            .For<CreateInvoice, int>(handler)
            .WithValidator(new CreateInvoiceValidator())
            .SendAsync(new CreateInvoice("", 0m));

        result.ShouldFailWithCodes("invoice.reference", "invoice.amount");
        handler.ShouldNotHaveRun();
    }

    [Fact]
    public async Task Without_a_validator_nothing_validates()
    {
        var result = await HandlerHarness
            .For<CreateInvoice, int>(new CreateInvoiceHandler(new InMemoryInvoiceStore()))
            .SendAsync(new CreateInvoice("", 0m));

        result.ShouldSucceed();
    }

    [Fact]
    public async Task HandleAsync_reaches_the_handler_without_the_pipeline()
    {
        var handler = StubHandler<CreateInvoice, int>.Returning(1);

        var result = await HandlerHarness
            .For<CreateInvoice, int>(handler)
            .WithBehavior(new ShortCircuitBehavior<CreateInvoice, int>(Refused))
            .HandleAsync(new CreateInvoice("INV-001", 250m));

        result.ShouldHaveValue(1);
        handler.ShouldHaveRun().Reference.Should().Be("INV-001");
    }

    [Fact]
    public async Task The_container_is_validated_the_way_the_application_validates_its_own()
    {
        var harness = HandlerHarness
            .For<CreateInvoice, int>(StubHandler<CreateInvoice, int>.Returning(1))
            .WithServices(services =>
            {
                services.AddScoped<IInvoiceStore, InMemoryInvoiceStore>();
                services.AddSingleton<CaptiveHolder>();
            });

        var send = async () => await harness.SendAsync(new CreateInvoice("INV-001", 250m));

        // A singleton holding a scoped service. Startup refuses it, and so does the harness.
        (await send.Should().ThrowAsync<AggregateException>())
            .Which.Message.Should().Contain("scoped");
    }

    [Fact]
    public async Task A_dependency_nobody_registered_fails_with_a_message_naming_it()
    {
        var harness = HandlerHarness
            .For<CreateInvoice, int>(services => new CreateInvoiceHandler(services.GetRequiredService<IInvoiceStore>()));

        var send = async () => await harness.SendAsync(new CreateInvoice("INV-001", 250m));

        (await send.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain(nameof(IInvoiceStore));
    }

    [Fact]
    public async Task Configuring_a_harness_that_has_already_run_is_refused()
    {
        var harness = HandlerHarness.For<CreateInvoice, int>(StubHandler<CreateInvoice, int>.Returning(1));

        await harness.SendAsync(new CreateInvoice("INV-001", 250m));

        var late = () => harness.WithBehavior(new ShortCircuitBehavior<CreateInvoice, int>(Refused));

        late.Should().Throw<InvalidOperationException>()
            .WithMessage("*already run*", "a behaviour added too late would silently not be in the pipeline");
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_handler()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var observed = CancellationToken.None;

        var handler = new StubHandler<CloseInvoice, Unit>((_, token) =>
        {
            observed = token;
            return Task.FromResult(Unit.Success);
        });

        await HandlerHarness.For<CloseInvoice, Unit>(handler).SendAsync(new CloseInvoice(1), cancellation.Token);

        observed.IsCancellationRequested.Should().BeTrue();
    }
}
