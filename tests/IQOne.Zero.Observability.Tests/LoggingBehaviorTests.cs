using IQOne.Zero.Messaging;
using IQOne.Zero.Observability;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// What the pipeline says about a request, and — more importantly — how loudly.
/// </summary>
/// <remarks>
/// The level is the part that has to be right. A log where every rejected form is a warning
/// is a log whose warnings mean nothing, and the cost of that is not paid until the night
/// somebody scrolls past a real one.
/// </remarks>
public class LoggingBehaviorTests
{
    private static async Task<LogSink> SendAsync(
        Result<string> answer, Action<ObservabilityOptions>? configure = null)
    {
        var application = TestApplication.With(configure);

        application.Handles<Ping>(() => answer);

        using var running = application.Build();

        await running.SendAsync(new Ping("hello"));

        return application.Log;
    }

    [Fact]
    public async Task An_unacceptable_request_is_a_normal_answer_and_not_a_warning_about_the_system()
    {
        var log = await SendAsync(Error.Validation("register.email", "An email address is required."));

        log.Single("ZeroRequestFailed").Level.Should().Be(LogLevel.Information);

        log.Lines.Should().NotContain(
            l => l.Level >= LogLevel.Warning,
            "a rejected request is the application working, and nothing about the system is wrong");
    }

    [Fact]
    public async Task A_missing_thing_is_a_normal_answer_too()
    {
        var log = await SendAsync(Error.NotFound("invoice.missing", "No such invoice."));

        log.Single("ZeroRequestFailed").Level.Should().Be(LogLevel.Information);
    }

    [Theory]
    [InlineData(ErrorKind.Conflict)]
    [InlineData(ErrorKind.Unauthorized)]
    [InlineData(ErrorKind.Forbidden)]
    public async Task Every_other_definite_no_is_a_normal_answer(ErrorKind kind)
    {
        var log = await SendAsync(new Error("some.code", "No.", kind));

        log.Single("ZeroRequestFailed").Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task An_unavailable_dependency_is_a_warning_because_it_is_usually_worth_retrying()
    {
        var log = await SendAsync(Error.Unavailable("ledger.timeout", "The ledger did not answer."));

        log.Single("ZeroRequestFailed").Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task An_unclassified_failure_is_an_error_because_nobody_expected_it()
    {
        var log = await SendAsync(Error.Failure("ledger.broken", "The ledger is in a state nobody planned for."));

        log.Single("ZeroRequestFailed").Level.Should().Be(LogLevel.Error);
    }

    [Fact]
    public async Task A_failure_line_names_the_code_the_kind_and_how_many_there_were()
    {
        var log = await SendAsync(Result<string>.Failure(
        [
            Error.Validation("register.email", "An email address is required."),
            Error.Validation("register.password", "Too short.")
        ]));

        var failed = log.Single("ZeroRequestFailed");

        failed.Message.Should().Contain("register.email").And.Contain("Validation").And.Contain("2 error(s)");

        // The second error is counted, not written out: a form with nine unacceptable fields
        // is one line, and the first code is enough to find the validator that produced it.
        failed.Message.Should().NotContain("register.password");
    }

    [Fact]
    public async Task A_success_is_one_line_carrying_how_long_it_took()
    {
        var log = await SendAsync(Result<string>.Success("answered"));

        var succeeded = log.Single("ZeroRequestSucceeded");

        succeeded.Level.Should().Be(LogLevel.Information);
        succeeded.Message.Should().Contain("Ping").And.Contain("250");
    }

    [Fact]
    public async Task The_category_is_the_request_type_so_one_request_can_be_turned_up_on_its_own()
    {
        var log = await SendAsync(Result<string>.Success("answered"));

        log.Lines.Should().OnlyContain(l => l.Category == typeof(Ping).FullName);
    }

    [Fact]
    public async Task What_the_caller_sent_stays_out_of_the_log()
    {
        var application = TestApplication.With();

        application.Handles<Register>(static () => Result<string>.Success("registered"));

        using var running = application.Build();

        await running.SendAsync(new Register("someone@example.com", "hunter2"));

        application.Log.Named("ZeroRequestContents").Should().BeEmpty();

        application.Log.Lines.Should().NotContain(
            l => l.Message.Contains("someone@example.com", StringComparison.Ordinal),
            "a command carries whatever the caller sent, and a log travels further than the database does");

        application.Log.Lines.Should().NotContain(l => l.Message.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task What_the_caller_sent_is_written_only_when_the_application_has_opted_in()
    {
        var application = TestApplication.With(options => options.LogRequestContents = true);

        application.Handles<Register>(static () => Result<string>.Success("registered"));

        using var running = application.Build();

        await running.SendAsync(new Register("someone@example.com", "hunter2"));

        var contents = application.Log.Single("ZeroRequestContents");

        contents.Message.Should().Contain("someone@example.com");

        // Debug even then, so appearing in production takes a second deliberate act.
        contents.Level.Should().Be(LogLevel.Debug);
    }

    [Fact]
    public async Task A_line_nobody_is_listening_to_is_never_built()
    {
        var application = TestApplication.With(options => options.LogRequestContents = true);

        application.Log.Minimum = LogLevel.Information;
        application.Handles<Unprintable>();

        using var running = application.Build();

        // Unprintable throws from ToString. Reaching this line at all is the assertion: source
        // generated logging checks the level before it formats anything, so a Debug line under
        // an Information filter costs one comparison. An interpolated string would have thrown.
        var result = await running.SendAsync(new Unprintable(7));

        result.IsSuccess.Should().BeTrue();
        application.Log.Named("ZeroRequestContents").Should().BeEmpty();
        application.Log.Named("ZeroRequestStarted").Should().BeEmpty();
    }

    [Fact]
    public async Task An_exception_that_escapes_is_an_error_and_still_escapes()
    {
        var application = TestApplication.With();

        var handler = application.Handles<Ping>();
        handler.Throws = new InvalidOperationException("the ledger is on fire");

        using var running = application.Build();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => running.SendAsync(new Ping("hello")));

        thrown.Message.Should().Be("the ledger is on fire");

        var threw = application.Log.Single("ZeroRequestThrew");

        threw.Level.Should().Be(LogLevel.Error);
        threw.Exception.Should().BeSameAs(thrown);
    }

    [Fact]
    public async Task A_caller_who_hangs_up_is_not_an_incident()
    {
        var application = TestApplication.With();

        using var cancellation = new CancellationTokenSource();

        var handler = application.Handles<Ping>();
        handler.Throws = new OperationCanceledException(cancellation.Token);

        using var running = application.Build();

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => running.SendAsync(new Ping("hello"), cancellation.Token));

        application.Log.Single("ZeroRequestCancelled").Level.Should().Be(LogLevel.Information);

        application.Log.Lines.Should().NotContain(
            l => l.Level >= LogLevel.Warning,
            "a client that hangs up under load must not inflate the error rate exactly when load is the problem");
    }

    [Fact]
    public async Task An_id_the_caller_issued_reaches_the_log()
    {
        var application = TestApplication.With();

        application.Handles<Ping>();

        using var running = application.Build();

        using (CorrelationId.Begin("batch-42"))
        {
            await running.SendAsync(new Ping("hello"));
        }

        var scopes = application.Log.Single("ZeroRequestSucceeded").Scopes
            .OfType<IEnumerable<KeyValuePair<string, object>>>()
            .SelectMany(s => s);

        scopes.Should().Contain(new KeyValuePair<string, object>(TelemetryTags.CorrelationId, "batch-42"));
    }

    [Fact]
    public async Task Without_an_id_from_outside_no_scope_is_opened()
    {
        var log = await SendAsync(Result<string>.Success("answered"));

        // The trace id already reaches every line through Activity.Current; repeating it here
        // would be a duplicated column on all of them.
        log.Single("ZeroRequestSucceeded").Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Switching_logging_off_writes_nothing_and_still_answers()
    {
        var log = await SendAsync(Result<string>.Success("answered"), options => options.EnableLogging = false);

        log.Lines.Should().BeEmpty();
    }

    [Fact]
    public void Logging_is_the_outermost_behaviour_so_nothing_finishes_unobserved()
    {
        var behavior = new LoggingBehavior<Ping, string>(
            new RecordingLogger<Ping>(new LogSink()), new ObservabilityOptions(), TimeProvider.System);

        behavior.Order.Should().Be(BehaviorOrder.Logging);
        behavior.Order.Should().BeLessThan(ObservabilityOrder.Tracing);
        behavior.Order.Should().BeLessThan(BehaviorOrder.Authorization);
    }
}
