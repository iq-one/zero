using System.Diagnostics;
using System.Diagnostics.Metrics;
using IQOne.Zero.Messaging;
using IQOne.Zero.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// An activity source and a meter are process-wide. Two test classes running at once would
// each record the other's requests, and the test that asserts nothing is produced when nobody
// is listening would fail for a reason that has nothing to do with the code.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace IQOne.Zero.Observability.Tests;

internal sealed record Ping(string Message) : IQuery<string>;

internal sealed record Register(string Email, string Password) : ICommand<string>;

/// <summary>
/// A request that refuses to describe itself.
/// </summary>
/// <remarks>
/// Used to prove that a switched-off line costs nothing: if the pipeline built the message
/// before asking whether anyone wanted it, this would throw instead of being skipped.
/// </remarks>
internal sealed record Unprintable(int Id) : IQuery<string>
{
    public override string ToString()
        => throw new InvalidOperationException("A line that nobody is listening to must not be formatted.");
}

/// <summary>Answers however the test told it to, and takes however long the test said.</summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="time">Advanced while handling, so a duration is stated rather than waited for.</param>
/// <param name="answer">What to answer with.</param>
internal sealed class ScriptedHandler<TRequest, TResponse>(SteppingTime time, Func<Result<TResponse>> answer)
    : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Calls { get; private set; }

    public TimeSpan Takes { get; set; } = TimeSpan.FromMilliseconds(250);

    public Exception? Throws { get; set; }

    public Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        time.Advance(Takes);

        if (Throws is { } thrown) throw thrown;

        return Task.FromResult(answer());
    }
}

/// <summary>
/// A clock the test moves by hand.
/// </summary>
/// <remarks>
/// The behaviours measure through <see cref="TimeProvider"/>, so a test can say a request took
/// a quarter of a second and assert on that exact number instead of sleeping and hoping.
/// </remarks>
internal sealed class SteppingTime : TimeProvider
{
    private long _timestamp;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    public void Advance(TimeSpan by) => _timestamp += by.Ticks;
}

/// <summary>One line a behaviour wrote.</summary>
/// <param name="Category">The logger's category, which is the request type.</param>
/// <param name="Level">The level it was written at.</param>
/// <param name="EventId">Which line this is.</param>
/// <param name="Message">The formatted message.</param>
/// <param name="Exception">The exception attached, when there was one.</param>
/// <param name="Scopes">The scopes open when it was written.</param>
internal sealed record LogLine(
    string Category,
    LogLevel Level,
    EventId EventId,
    string Message,
    Exception? Exception,
    IReadOnlyList<object?> Scopes);

/// <summary>Everything every logger in the application wrote.</summary>
/// <remarks>
/// A single sink rather than one per category, because the interesting assertions are about
/// what the application as a whole did and did not say.
/// </remarks>
internal sealed class LogSink
{
    private readonly List<LogLine> _lines = [];

    /// <summary>Nothing below this is written, the way a host's filter would have it.</summary>
    public LogLevel Minimum { get; set; } = LogLevel.Trace;

    public IReadOnlyList<LogLine> Lines
    {
        get { lock (_lines) return [.. _lines]; }
    }

    public IEnumerable<LogLine> Named(string eventName) => Lines.Where(l => l.EventId.Name == eventName);

    public LogLine Single(string eventName) => Named(eventName).Should().ContainSingle().Subject;

    public void Add(LogLine line)
    {
        lock (_lines) _lines.Add(line);
    }
}

/// <summary>Records what was written instead of writing it anywhere.</summary>
/// <typeparam name="TCategory">The category the logger belongs to.</typeparam>
/// <param name="sink">Where the lines are collected.</param>
internal sealed class RecordingLogger<TCategory>(LogSink sink) : ILogger<TCategory>
{
    private readonly List<object?> _scopes = [];

    public IDisposable BeginScope<TState>(TState state) where TState : notnull
    {
        lock (_scopes) _scopes.Add(state);

        return new Closing(_scopes, state);
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= sink.Minimum;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        object?[] open;
        lock (_scopes) open = [.. _scopes];

        sink.Add(new LogLine(
            typeof(TCategory).FullName ?? typeof(TCategory).Name,
            logLevel,
            eventId,
            formatter(state, exception),
            exception,
            open));
    }

    private sealed class Closing(List<object?> scopes, object state) : IDisposable
    {
        public void Dispose()
        {
            lock (scopes) scopes.Remove(state);
        }
    }
}

/// <summary>Collects the activities the tracing behaviour produced.</summary>
/// <remarks>
/// An <see cref="ActivityListener"/> is what a real collector uses, so subscribing the same
/// way is the only assertion that proves a consumer would actually see these spans.
/// </remarks>
internal sealed class ActivityRecorder : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _stopped = [];

    public ActivityRecorder()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ZeroTelemetry.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _)
                => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_stopped) _stopped.Add(activity);
            }
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Stopped
    {
        get { lock (_stopped) return [.. _stopped]; }
    }

    /// <summary>Only the activities this test caused, so a neighbouring test cannot fail it.</summary>
    /// <param name="name">The activity name, which is the request's short type name.</param>
    /// <returns>What was recorded for that request.</returns>
    public IReadOnlyList<Activity> For(string name) => [.. Stopped.Where(a => a.OperationName == name)];

    public void Dispose() => _listener.Dispose();
}

/// <summary>One value written to one instrument.</summary>
/// <param name="Instrument">The instrument's name.</param>
/// <param name="Value">What was recorded.</param>
/// <param name="Tags">What it was recorded against.</param>
internal sealed record Measurement(string Instrument, double Value, IReadOnlyList<KeyValuePair<string, object?>> Tags)
{
    public object? Tag(string name) => Tags.FirstOrDefault(t => t.Key == name).Value;
}

/// <summary>Collects the measurements the metrics behaviour recorded.</summary>
/// <remarks>
/// Through <see cref="MeterListener"/>, for the same reason the activities go through an
/// <see cref="ActivityListener"/>: an instrument that a listener cannot reach is an
/// instrument no dashboard will ever show.
/// </remarks>
internal sealed class MeasurementRecorder : IDisposable
{
    private readonly MeterListener _listener;
    private readonly List<Measurement> _measurements = [];

    public MeasurementRecorder()
    {
        _listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ZeroTelemetry.MeterName) listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));

        _listener.Start();
    }

    public IReadOnlyList<Measurement> Measurements
    {
        get { lock (_measurements) return [.. _measurements]; }
    }

    /// <summary>What one instrument recorded for one request, ignoring every other test's traffic.</summary>
    /// <param name="instrument">The instrument's name.</param>
    /// <param name="request">The request's short type name.</param>
    /// <returns>The matching measurements.</returns>
    public IReadOnlyList<Measurement> For(string instrument, string request) =>
    [
        .. Measurements.Where(m =>
            m.Instrument == instrument && Equals(m.Tag(TelemetryTags.RequestName), request))
    ];

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copied = new KeyValuePair<string, object?>[tags.Length];
        tags.CopyTo(copied);

        lock (_measurements) _measurements.Add(new Measurement(instrument.Name, value, copied));
    }
}

/// <summary>
/// Assembles the real pipeline around a handler.
/// </summary>
/// <remarks>
/// Observability is only worth having if a handler cannot opt out of it, so every test sends
/// through <see cref="ISender"/> rather than calling a behaviour directly.
/// </remarks>
internal sealed class TestApplication
{
    private readonly ServiceCollection _services = [];
    private readonly List<RequestEntry> _entries = [];

    private TestApplication(Action<ObservabilityOptions>? configure)
    {
        _services.AddSingleton(Log);

        // As TimeProvider, which is what the behaviours ask for: registered as its own type it
        // would sit in the container next to the real clock rather than instead of it.
        _services.AddSingleton<TimeProvider>(Time);
        _services.AddSingleton(typeof(ILogger<>), typeof(RecordingLogger<>));
        _services.AddZeroObservability(configure);
    }

    public LogSink Log { get; } = new();

    public SteppingTime Time { get; } = new();

    public static TestApplication With(Action<ObservabilityOptions>? configure = null) => new(configure);

    public ScriptedHandler<TRequest, string> Handles<TRequest>(Func<Result<string>>? answer = null)
        where TRequest : IRequest<string>
    {
        var handler = new ScriptedHandler<TRequest, string>(
            Time, answer ?? (static () => Result<string>.Success("answered")));

        _services.AddScoped<IRequestHandler<TRequest, string>>(_ => handler);

        _entries.Add(new RequestEntry(
            typeof(TRequest), typeof(string), handler.GetType(),
            static (sp, request, ct) => RequestPipeline.RunAsync<TRequest, string>((TRequest)request, sp, ct)));

        return handler;
    }

    public RunningApplication Build()
    {
        _services.AddZeroMessagingWithRequests(requests =>
        {
            foreach (var entry in _entries) requests.Add(entry);
        });

        return new RunningApplication(_services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
    }
}

/// <summary>Sends each request from its own scope, the way a host would.</summary>
/// <param name="provider">The built container.</param>
internal sealed class RunningApplication(ServiceProvider provider) : IDisposable
{
    public async Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(request, cancellationToken);
    }

    public void Dispose() => provider.Dispose();
}
