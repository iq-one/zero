using IQOne.Zero.Events;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Events.Tests;

/// <summary>
/// Publishing a fact. The behaviour that matters is what happens to the subscribers that
/// are not the one that broke — an event has already happened, so nothing about it may be
/// conditional on who managed to keep up.
/// </summary>
public class PublishTests
{
    private static (IPublisher Publisher, List<string> Log) Build(
        Action<EventOptions>? configure = null,
        params Func<List<string>, IEventHandler<InvoicePaid>>[] subscribers)
    {
        var log = new List<string>();
        var services = new ServiceCollection();

        foreach (var subscriber in subscribers)
            services.AddScoped<IEventHandler<InvoicePaid>>(_ => subscriber(log));

        services.AddZeroEventsWithHandlers(builder => builder.Add(new EventEntry(
            typeof(InvoicePaid),
            [.. subscribers.Select(s => s(new List<string>()).GetType())],
            static (provider, @event, token) =>
                EventDispatch.RunAsync<InvoicePaid>((InvoicePaid)@event, provider, token))), configure);

        var provider = services.BuildServiceProvider();

        return (provider.GetRequiredService<IPublisher>(), log);
    }

    [Fact]
    public async Task Every_subscriber_runs_and_each_outcome_comes_back()
    {
        var (publisher, log) = Build(null, l => new Recording(l, "ledger"), l => new Recording(l, "email"));

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.IsSuccess.Should().BeTrue();
        published.Outcomes.Should().HaveCount(2);
        log.Should().BeEquivalentTo(["ledger", "email"]);
    }

    [Fact]
    public async Task A_failing_subscriber_does_not_stop_the_others()
    {
        var (publisher, log) = Build(
            null, l => new Failing(l), l => new Recording(l, "email"));

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.IsFailure.Should().BeTrue();
        log.Should().Contain("email",
            "the fact already happened; a subscriber that could not keep up must not deprive the rest of it");
    }

    [Fact]
    public async Task The_result_says_WHICH_subscriber_failed_not_only_that_one_did()
    {
        var (publisher, _) = Build(null, l => new Failing(l), l => new Recording(l, "email"));

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.Outcomes.Single(o => o.IsFailure).HandlerType.Should().Be<Failing>();
        published.Errors.Should().ContainSingle().Which.Should().Be(Failing.Behind);
    }

    [Fact]
    public async Task A_throwing_subscriber_is_captured_and_the_rest_still_run()
    {
        var (publisher, log) = Build(null, l => new Throwing(l), l => new Recording(l, "email"));

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.IsFailure.Should().BeTrue();
        log.Should().Contain("email");

        published.Outcomes.Single(o => o.HandlerType == typeof(Throwing))
            .Exception.Should().BeOfType<InvalidOperationException>(
                "a throw is a defect, so the exception is kept whole rather than flattened into an error");
    }

    [Fact]
    public async Task Stopping_at_the_first_failure_is_available_when_the_application_asks_for_it()
    {
        var (publisher, log) = Build(
            options => options.OnHandlerFailure = HandlerFailure.Stop,
            l => new Failing(l), l => new Recording(l, "email"));

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.IsFailure.Should().BeTrue();
        log.Should().NotContain("email");
    }

    [Fact]
    public async Task Publishing_to_nobody_succeeds()
    {
        var (publisher, _) = Build();

        var published = await publisher.PublishAsync(new InvoicePaid(1, 50m), CancellationToken.None);

        published.IsSuccess.Should().BeTrue("a fact nobody has needed yet is not a failure");
        published.Outcomes.Should().BeEmpty();
    }
}
