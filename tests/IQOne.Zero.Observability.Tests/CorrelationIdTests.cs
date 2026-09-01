using IQOne.Zero.Observability;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// The one id that ties a request's records together, and the rule that it is the trace id
/// unless a caller brought their own.
/// </summary>
public class CorrelationIdTests
{
    [Fact]
    public void With_nothing_supplied_the_current_id_is_the_trace_id()
    {
        using var recorder = new ActivityRecorder();
        using var activity = ZeroTelemetry.Source.StartActivity("Reading");

        activity.Should().NotBeNull();

        CorrelationId.Current.Should().Be(activity!.TraceId.ToString());
        CorrelationId.Supplied.Should().BeNull();
    }

    [Fact]
    public void An_id_from_outside_wins_over_the_trace_id()
    {
        using var recorder = new ActivityRecorder();
        using var activity = ZeroTelemetry.Source.StartActivity("Reading");

        using (CorrelationId.Begin("batch-42"))
        {
            // The caller is tracking this work under a name of their own, and answering to a
            // system that has never heard of a trace id.
            CorrelationId.Current.Should().Be("batch-42");
            CorrelationId.Supplied.Should().Be("batch-42");
        }
    }

    [Fact]
    public void The_previous_id_comes_back_when_a_scope_ends()
    {
        using (CorrelationId.Begin("outer"))
        {
            using (CorrelationId.Begin("inner"))
            {
                CorrelationId.Supplied.Should().Be("inner");
            }

            CorrelationId.Supplied.Should().Be("outer");
        }

        CorrelationId.Supplied.Should().BeNull();
    }

    [Fact]
    public void Disposing_a_scope_twice_does_not_unwind_the_one_above_it()
    {
        using (CorrelationId.Begin("outer"))
        {
            var inner = CorrelationId.Begin("inner");

            inner.Dispose();
            inner.Dispose();

            CorrelationId.Supplied.Should().Be("outer");
        }
    }

    [Fact]
    public async Task The_id_reaches_everything_awaited_inside_the_scope()
    {
        static async Task<string?> Deeper()
        {
            await Task.Yield();

            return CorrelationId.Supplied;
        }

        using (CorrelationId.Begin("batch-42"))
        {
            (await Deeper()).Should().Be("batch-42");
        }
    }

    [Fact]
    public async Task An_id_started_further_in_does_not_leak_back_out()
    {
        static async Task Started()
        {
            CorrelationId.Begin("inner").Dispose();

            await Task.Yield();
        }

        using (CorrelationId.Begin("outer"))
        {
            await Started();

            // Two requests handled at once must never see each other's id, and this is the
            // guarantee that makes that true: a value set further in does not flow back out.
            CorrelationId.Supplied.Should().Be("outer");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_id_with_no_value_is_refused(string id)
    {
        var refused = Assert.Throws<ArgumentException>(() => CorrelationId.Begin(id));

        refused.ParamName.Should().Be("id");
    }
}
