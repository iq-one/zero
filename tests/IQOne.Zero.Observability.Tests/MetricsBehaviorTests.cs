using System.Diagnostics.Metrics;
using IQOne.Zero.Messaging;
using IQOne.Zero.Observability;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// What a request adds to the counter and the histogram, seen through a
/// <see cref="MeterListener"/>.
/// </summary>
/// <remarks>
/// The outcome tag is the point. An error rate that counts every rejected form never reaches
/// zero, so nobody alerts on it, so nobody notices the morning it climbs.
/// </remarks>
public class MetricsBehaviorTests
{
    private static async Task<MeasurementRecorder> SendAsync(
        Result<string> answer, Action<ObservabilityOptions>? configure = null)
    {
        var recorder = new MeasurementRecorder();

        var application = TestApplication.With(configure);

        application.Handles<Ping>(() => answer);

        using var running = application.Build();

        await running.SendAsync(new Ping("hello"));

        return recorder;
    }

    [Fact]
    public async Task Every_request_is_counted_once_by_name_and_outcome()
    {
        using var recorder = await SendAsync(Result<string>.Success("answered"));

        var counted = recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping))
            .Should().ContainSingle().Subject;

        counted.Value.Should().Be(1);
        counted.Tag(TelemetryTags.Outcome).Should().Be("success");
        counted.Tag(TelemetryTags.ErrorType).Should().BeNull();
    }

    [Fact]
    public async Task The_duration_is_recorded_in_seconds()
    {
        using var recorder = await SendAsync(Result<string>.Success("answered"));

        var timed = recorder.For(ZeroTelemetry.RequestDurationName, nameof(Ping))
            .Should().ContainSingle().Subject;

        // The handler advanced the clock by 250ms. Seconds because that is what the
        // OpenTelemetry conventions specify for a duration; the log line says milliseconds,
        // because a human reads that one.
        timed.Value.Should().Be(0.25);
        timed.Tag(TelemetryTags.Outcome).Should().Be("success");
    }

    [Fact]
    public async Task Both_instruments_carry_the_same_tags_so_how_many_and_how_slow_slice_alike()
    {
        using var recorder = await SendAsync(Error.Unavailable("ledger.timeout", "The ledger did not answer."));

        var counted = recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Single();
        var timed = recorder.For(ZeroTelemetry.RequestDurationName, nameof(Ping)).Single();

        timed.Tags.Should().BeEquivalentTo(counted.Tags);
    }

    [Fact]
    public async Task A_definite_no_is_counted_as_rejected_and_not_as_a_fault()
    {
        using var recorder = await SendAsync(Error.Validation("register.email", "An email address is required."));

        var counted = recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Single();

        counted.Tag(TelemetryTags.Outcome).Should().Be("rejected");
        counted.Tag(TelemetryTags.ErrorType).Should().Be("register.email");
    }

    [Fact]
    public async Task An_unavailable_dependency_is_counted_as_a_fault()
    {
        using var recorder = await SendAsync(Error.Unavailable("ledger.timeout", "The ledger did not answer."));

        recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Single()
            .Tag(TelemetryTags.Outcome).Should().Be("faulted");
    }

    [Fact]
    public async Task An_escaped_exception_is_counted_as_a_fault_named_by_its_type()
    {
        using var recorder = new MeasurementRecorder();

        var application = TestApplication.With();

        var handler = application.Handles<Ping>();
        handler.Throws = new InvalidOperationException("the ledger is on fire");

        using var running = application.Build();

        await Assert.ThrowsAsync<InvalidOperationException>(() => running.SendAsync(new Ping("hello")));

        var counted = recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Single();

        counted.Tag(TelemetryTags.Outcome).Should().Be("faulted");
        counted.Tag(TelemetryTags.ErrorType).Should().Be(typeof(InvalidOperationException).FullName);
    }

    [Fact]
    public async Task A_caller_who_hangs_up_is_not_counted_as_a_fault()
    {
        using var recorder = new MeasurementRecorder();
        using var cancellation = new CancellationTokenSource();

        var application = TestApplication.With();

        var handler = application.Handles<Ping>();
        handler.Throws = new OperationCanceledException(cancellation.Token);

        using var running = application.Build();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.SendAsync(new Ping("hello"), cancellation.Token));

        recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Single()
            .Tag(TelemetryTags.Outcome).Should().Be("cancelled");
    }

    [Fact]
    public async Task Switching_metrics_off_records_nothing()
    {
        using var recorder = await SendAsync(
            Result<string>.Success("answered"), options => options.EnableMetrics = false);

        recorder.For(ZeroTelemetry.RequestCountName, nameof(Ping)).Should().BeEmpty();
        recorder.For(ZeroTelemetry.RequestDurationName, nameof(Ping)).Should().BeEmpty();
    }

    [Fact]
    public void The_instruments_are_on_the_meter_a_consumer_is_told_to_subscribe_to()
    {
        ZeroTelemetry.MeterName.Should().Be("IQOne.Zero.Observability");
        ZeroTelemetry.ActivitySourceName.Should().Be("IQOne.Zero.Observability");

        ZeroTelemetry.RequestCount.Meter.Name.Should().Be(ZeroTelemetry.MeterName);
        ZeroTelemetry.RequestDuration.Meter.Name.Should().Be(ZeroTelemetry.MeterName);
        ZeroTelemetry.RequestDuration.Unit.Should().Be("s");
    }

    [Fact]
    public void Metrics_is_the_innermost_of_the_three_so_its_measurement_covers_the_rest()
    {
        var behavior = new MetricsBehavior<Ping, string>(new ObservabilityOptions(), TimeProvider.System);

        behavior.Order.Should().Be(ObservabilityOrder.Metrics);
        behavior.Order.Should().BeGreaterThan(ObservabilityOrder.Tracing);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Authorization);
    }
}
