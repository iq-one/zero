using IQOne.Zero;

namespace IQOne.Zero.Results.Tests;

public class ResultTests
{
    [Fact]
    public void A_default_result_is_a_failure_so_a_forgotten_assignment_cannot_pass_silently()
    {
        Result result = default;

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void A_default_result_of_T_explains_itself_when_its_value_is_read()
    {
        Result<int> result = default;

        var read = () => result.Value;

        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*never initialised*");
    }

    [Fact]
    public void A_value_converts_to_a_successful_result()
    {
        Result<int> result = 42;

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void An_error_converts_to_a_failed_result()
    {
        Result<int> result = Error.NotFound("thing.missing", "No such thing.");

        result.IsFailure.Should().BeTrue();
        result.Error.Kind.Should().Be(ErrorKind.NotFound);
        result.Error.Code.Should().Be("thing.missing");
    }

    [Fact]
    public void Reading_the_value_of_a_failure_says_which_failure()
    {
        Result<int> result = Error.Conflict("order.closed", "This order is already closed.");

        var read = () => result.Value;

        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*order.closed*");
    }

    [Fact]
    public void TryGetValue_reports_the_outcome_without_throwing()
    {
        Result<int> failure = Error.Failure("x", "y");

        failure.TryGetValue(out var value).Should().BeFalse();
        value.Should().Be(default);

        Result<int> success = 7;

        success.TryGetValue(out var got).Should().BeTrue();
        got.Should().Be(7);
    }

    [Fact]
    public void A_failed_result_must_carry_a_reason()
    {
        var create = () => Result.Failure([]);

        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Combine_collects_every_reason_rather_than_stopping_at_the_first()
    {
        var combined = Result.Combine(
            Result.Success(),
            Error.Validation("name", "Name is required."),
            Error.Validation("email", "Email is not valid."));

        combined.IsFailure.Should().BeTrue();
        combined.Errors.Select(e => e.Code).Should().Equal("name", "email");
    }

    [Fact]
    public void A_result_of_T_narrows_to_a_result_keeping_the_errors()
    {
        Result<int> typed = Error.Forbidden("nope", "Not allowed.");

        Result narrowed = typed;

        narrowed.IsFailure.Should().BeTrue();
        narrowed.Error.Code.Should().Be("nope");
    }
}
