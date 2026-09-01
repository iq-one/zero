using IQOne.Zero.Observability;
using Microsoft.Extensions.Logging;

namespace IQOne.Zero.Observability.Tests;

/// <summary>
/// The judgement the three behaviours share.
/// </summary>
/// <remarks>
/// It is made once so that logging, tracing and metrics cannot disagree. If the log called a
/// not-found a warning while the counter called it a success, neither signal could be trusted
/// against the other, and the usual outcome is that both are ignored.
/// </remarks>
public class RequestOutcomeTests
{
    [Theory]
    [InlineData(ErrorKind.Validation)]
    [InlineData(ErrorKind.NotFound)]
    [InlineData(ErrorKind.Conflict)]
    [InlineData(ErrorKind.Unauthorized)]
    [InlineData(ErrorKind.Forbidden)]
    public void A_definite_no_is_the_application_working(ErrorKind kind)
    {
        kind.ToOutcome().Should().Be(RequestOutcome.Rejected);
        kind.ToLogLevel().Should().Be(LogLevel.Information);
    }

    [Fact]
    public void An_unavailable_dependency_is_a_fault_worth_a_warning()
    {
        ErrorKind.Unavailable.ToOutcome().Should().Be(RequestOutcome.Faulted);

        // A warning rather than an error: it says a dependency blinked and the operation is
        // usually worth retrying.
        ErrorKind.Unavailable.ToLogLevel().Should().Be(LogLevel.Warning);
    }

    [Fact]
    public void An_unclassified_failure_is_a_fault_worth_an_error()
    {
        ErrorKind.Failure.ToOutcome().Should().Be(RequestOutcome.Faulted);

        // Nobody classified it, which means nobody expected it.
        ErrorKind.Failure.ToLogLevel().Should().Be(LogLevel.Error);
    }

    [Theory]
    [InlineData(RequestOutcome.Success, "success")]
    [InlineData(RequestOutcome.Rejected, "rejected")]
    [InlineData(RequestOutcome.Faulted, "faulted")]
    [InlineData(RequestOutcome.Cancelled, "cancelled")]
    public void The_tag_value_is_written_out_rather_than_taken_from_the_member_name(
        RequestOutcome outcome, string tag)
    {
        // Dashboards and alert rules match these strings and nobody recompiles them, so
        // renaming a member must not silently change what those rules match.
        outcome.ToTagValue().Should().Be(tag);
    }
}
