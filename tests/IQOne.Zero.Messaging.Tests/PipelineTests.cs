using IQOne.Zero.Messaging;
using IQOne.Zero.Results;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Messaging.Tests;

/// <summary>
/// The pipeline is where validation, authorization, caching and transactions live, so its
/// ordering is load-bearing rather than cosmetic.
/// </summary>
public class PipelineTests
{
    private static ISender Sender(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();

        services.AddScoped<IRequestHandler<Greet, string>, GreetHandler>();

        var registry = new RequestRegistry();

        registry.Add(new RequestEntry(typeof(Greet), typeof(string), typeof(GreetHandler),
            static (sp, request, ct) => RequestPipeline.RunAsync<Greet, string>((Greet)request, sp, ct)));

        registry.Freeze();

        services.AddSingleton(registry);
        services.AddScoped<ISender, Sender>();

        configure(services);

        return services.BuildServiceProvider().GetRequiredService<ISender>();
    }

    [Fact]
    public async Task Behaviours_wrap_the_handler_lowest_order_outermost()
    {
        var log = new List<string>();

        var sender = Sender(services =>
        {
            services.AddScoped<IPipelineBehavior<Greet, string>>(
                _ => new RecordingBehavior<Greet, string>(log, "inner", 10));

            services.AddScoped<IPipelineBehavior<Greet, string>>(
                _ => new RecordingBehavior<Greet, string>(log, "outer", -10));
        });

        await sender.SendAsync(new Greet("x"), CancellationToken.None);

        log.Should().Equal("outer:in", "inner:in", "inner:out", "outer:out");
    }

    [Fact]
    public async Task Registration_order_does_not_decide_pipeline_order()
    {
        var log = new List<string>();

        var sender = Sender(services =>
        {
            // Registered outer-last on purpose: only Order may decide.
            services.AddScoped<IPipelineBehavior<Greet, string>>(
                _ => new RecordingBehavior<Greet, string>(log, "inner", BehaviorOrder.Transaction));

            services.AddScoped<IPipelineBehavior<Greet, string>>(
                _ => new RecordingBehavior<Greet, string>(log, "outer", BehaviorOrder.Logging));
        });

        await sender.SendAsync(new Greet("x"), CancellationToken.None);

        log.First().Should().Be("outer:in");
    }

    [Fact]
    public async Task A_behaviour_may_stop_the_pipeline_before_the_handler()
    {
        var refused = Error.Validation("greet.name", "A name is required.");

        var sender = Sender(services => services.AddScoped<IPipelineBehavior<Greet, string>>(
            _ => new ShortCircuitBehavior<Greet, string>(refused)));

        var result = await sender.SendAsync(new Greet(""), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(refused);
    }

    [Fact]
    public async Task A_request_with_no_behaviours_reaches_the_handler_directly()
    {
        var result = await Sender(_ => { }).SendAsync(new Greet("Zero"), CancellationToken.None);

        result.Value.Should().Be("Hello, Zero.");
    }
}
