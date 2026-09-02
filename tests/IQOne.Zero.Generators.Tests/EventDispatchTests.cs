using IQOne.Zero.Generators.Tests.Harness;

namespace IQOne.Zero.Generators.Tests;

/// <summary>
/// The compile-time half of events. An event has any number of subscribers, so unlike the
/// request table this one groups rather than rejecting a second registration.
/// </summary>
public class EventDispatchTests
{
    private const string Preamble = """
        using System.Threading;
        using System.Threading.Tasks;
        using IQOne.Zero;
        using IQOne.Zero.Events;

        namespace Test;
        """;

    [Fact]
    public void Two_subscribers_to_one_event_become_one_row_naming_both()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record InvoicePaid(int Id) : IEvent;

            public sealed class UpdateLedger : IEventHandler<InvoicePaid>
            {
                public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Success());
            }

            public sealed class EmailCustomer : IEventHandler<InvoicePaid>
            {
                public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Success());
            }
            """);

        run.HasError.Should().BeFalse();
        run.GeneratedFileErrors.Should().BeEmpty();

        run.GeneratedSource.Should()
            .Contain("EventDispatch.RunAsync<global::Test.InvoicePaid>")
            .And.Contain("typeof(global::Test.EmailCustomer)")
            .And.Contain("typeof(global::Test.UpdateLedger)");

        // One row, not one per subscriber: an event has many, and the table merges them.
        run.Occurrences("builder.Add(new global::IQOne.Zero.Events.EventEntry(").Should().Be(1);
    }

    [Fact]
    public void A_subscriber_is_registered_under_the_closed_interface_delivery_resolves()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record InvoicePaid(int Id) : IEvent;

            public sealed class UpdateLedger : IEventHandler<InvoicePaid>
            {
                public Task<Result> HandleAsync(InvoicePaid @event, CancellationToken cancellationToken)
                    => Task.FromResult(Result.Success());
            }
            """);

        run.GeneratedSource.Should().Contain(
            "AddScoped<global::IQOne.Zero.Events.IEventHandler<global::Test.InvoicePaid>, global::Test.UpdateLedger>");
    }

    [Fact]
    public void An_event_nobody_subscribes_to_declares_itself()
    {
        var run = GeneratorHarness.Run($$"""
            {{Preamble}}

            public sealed record NobodyCares : IEvent;
            """);

        run.GeneratedSource.Should().Contain("builder.Declare(typeof(global::Test.NobodyCares));");
    }

    [Fact]
    public void An_assembly_that_does_not_reference_events_generates_no_delivery_code()
    {
        var run = GeneratorHarness.RunWithout("""
            using IQOne.Zero.DependencyInjection.Descriptors;

            namespace Test;

            public interface IThing;

            public sealed class Thing : IThing, IScoped;
            """, "IQOne.Zero.Events");

        run.GeneratedSource.Should().NotContain("RegisterEvents",
            "an application with no events must pay nothing for them");
    }
}
