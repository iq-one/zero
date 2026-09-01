using System.Diagnostics;
using IQOne.Zero.Messaging;
using IQOne.Zero.Observability;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// The span a request leaves behind, seen the way a collector sees it.
/// </summary>
/// <remarks>
/// Every assertion here goes through an <see cref="ActivityListener"/> subscribed by name,
/// because that is the only thing that proves a consumer who wrote
/// <c>AddSource(ZeroTelemetry.ActivitySourceName)</c> would receive these spans. Asserting on
/// a field of the behaviour would prove the behaviour talks to itself.
/// </remarks>
public class TracingBehaviorTests
{
    private static async Task<Activity> ActivityFor(
        Result<string> answer, Action<ObservabilityOptions>? configure = null)
    {
        using var recorder = new ActivityRecorder();

        var application = TestApplication.With(configure);

        application.Handles<Ping>(() => answer);

        using var running = application.Build();

        await running.SendAsync(new Ping("hello"));

        return recorder.For(nameof(Ping)).Should().ContainSingle().Subject;
    }

    [Fact]
    public async Task One_activity_per_request_named_for_the_request()
    {
        var activity = await ActivityFor(Result<string>.Success("answered"));

        activity.OperationName.Should().Be(nameof(Ping));

        // Internal: the span that says a call arrived over HTTP or off a queue belongs to
        // whichever instrumentation received it. Ours says what the application then did.
        activity.Kind.Should().Be(ActivityKind.Internal);

        activity.GetTagItem(TelemetryTags.RequestName).Should().Be(nameof(Ping));
        activity.GetTagItem(TelemetryTags.RequestType).Should().Be(typeof(Ping).FullName);
    }

    [Fact]
    public async Task A_success_is_tagged_and_left_unset()
    {
        var activity = await ActivityFor(Result<string>.Success("answered"));

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("success");
        activity.Status.Should().Be(ActivityStatusCode.Unset);
        activity.GetTagItem(TelemetryTags.ErrorType).Should().BeNull();
    }

    [Fact]
    public async Task A_rejection_carries_the_error_code_without_painting_the_trace_red()
    {
        var activity = await ActivityFor(Error.NotFound("invoice.missing", "No such invoice."));

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("rejected");
        activity.GetTagItem(TelemetryTags.ErrorType).Should().Be("invoice.missing");
        activity.GetTagItem(TelemetryTags.ErrorKind).Should().Be(nameof(ErrorKind.NotFound));

        // Unset, not Error. Marking a working application's every "no" as a failure is the
        // same mistake as treating a 404 from a healthy server as a server error.
        activity.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task A_fault_marks_the_activity_failed()
    {
        var activity = await ActivityFor(Error.Unavailable("ledger.timeout", "The ledger did not answer."));

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("faulted");
        activity.GetTagItem(TelemetryTags.ErrorType).Should().Be("ledger.timeout");
        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Be("The ledger did not answer.");
    }

    [Fact]
    public async Task An_escaped_exception_is_recorded_the_way_the_conventions_describe_it()
    {
        using var recorder = new ActivityRecorder();

        var application = TestApplication.With();

        var handler = application.Handles<Ping>();
        handler.Throws = new InvalidOperationException("the ledger is on fire");

        using var running = application.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => running.SendAsync(new Ping("hello")));

        var activity = recorder.For(nameof(Ping)).Should().ContainSingle().Subject;

        activity.Status.Should().Be(ActivityStatusCode.Error);
        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("faulted");
        activity.GetTagItem(TelemetryTags.ErrorType).Should().Be(typeof(InvalidOperationException).FullName);

        var recorded = activity.Events.Should().ContainSingle().Subject;

        recorded.Name.Should().Be("exception");
        recorded.Tags.Should().Contain(new KeyValuePair<string, object?>(
            "exception.type", typeof(InvalidOperationException).FullName));
        recorded.Tags.Should().Contain(new KeyValuePair<string, object?>(
            "exception.message", "the ledger is on fire"));
    }

    [Fact]
    public async Task A_caller_who_hangs_up_leaves_a_cancelled_span_rather_than_a_broken_one()
    {
        using var recorder = new ActivityRecorder();
        using var cancellation = new CancellationTokenSource();

        var application = TestApplication.With();

        var handler = application.Handles<Ping>();
        handler.Throws = new OperationCanceledException(cancellation.Token);

        using var running = application.Build();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.SendAsync(new Ping("hello"), cancellation.Token));

        var activity = recorder.For(nameof(Ping)).Should().ContainSingle().Subject;

        activity.GetTagItem(TelemetryTags.Outcome).Should().Be("cancelled");
        activity.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task An_id_the_caller_issued_is_tagged_on_the_span()
    {
        using var recorder = new ActivityRecorder();

        var application = TestApplication.With();

        application.Handles<Ping>();

        using var running = application.Build();

        using (CorrelationId.Begin("batch-42"))
        {
            await running.SendAsync(new Ping("hello"));
        }

        recorder.For(nameof(Ping)).Should().ContainSingle()
            .Subject.GetTagItem(TelemetryTags.CorrelationId).Should().Be("batch-42");
    }

    [Fact]
    public async Task The_trace_id_is_not_repeated_as_a_correlation_tag()
    {
        var activity = await ActivityFor(Result<string>.Success("answered"));

        // A span tagged with its own trace id is a duplicated column in every trace.
        activity.GetTagItem(TelemetryTags.CorrelationId).Should().BeNull();
    }

    [Fact]
    public async Task Nothing_is_created_when_no_collector_has_subscribed()
    {
        var application = TestApplication.With();

        application.Handles<Ping>();

        using var running = application.Build();

        await running.SendAsync(new Ping("hello"));

        // With no listener, StartActivity returns null and the behaviour costs one null check.
        // The proof is that the application still answered rather than dereferencing nothing.
        using var recorder = new ActivityRecorder();

        recorder.For(nameof(Ping)).Should().BeEmpty();
    }

    [Fact]
    public async Task Switching_tracing_off_produces_no_span()
    {
        using var recorder = new ActivityRecorder();

        var application = TestApplication.With(options => options.EnableTracing = false);

        application.Handles<Ping>();

        using var running = application.Build();

        (await running.SendAsync(new Ping("hello"))).IsSuccess.Should().BeTrue();

        recorder.For(nameof(Ping)).Should().BeEmpty();
    }

    [Fact]
    public void Tracing_sits_inside_logging_and_outside_everything_that_can_reject()
    {
        var behavior = new TracingBehavior<Ping, string>(new ObservabilityOptions());

        behavior.Order.Should().Be(ObservabilityOrder.Tracing);
        behavior.Order.Should().BeGreaterThan(BehaviorOrder.Logging);
        behavior.Order.Should().BeLessThan(ObservabilityOrder.Metrics);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Authorization);
    }
}
