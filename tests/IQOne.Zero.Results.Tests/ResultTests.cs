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

    [Fact]
    public void A_failure_always_states_a_reason_even_when_it_was_never_given_one()
    {
        // Everything downstream reads the first error — a status code, a log line, a problem
        // response. A failure with none of them is a NullReference waiting at the edge.
        Result untyped = default;
        Result<int> typed = default;

        untyped.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Uninitialised.Code);
        typed.Errors.Should().ContainSingle().Which.Code.Should().Be(Error.Uninitialised.Code);
        untyped.Error.IsNone.Should().BeFalse();
    }

    [Fact]
    public void Combine_decides_on_the_outcome_and_not_on_the_number_of_errors()
    {
        // R1: Combine used to succeed here. A default Result is a failure that carried no
        // errors, the count was zero, and the failed operand vanished — the one thing this
        // package exists to prevent.
        var combined = Result.Combine(Result.Success(), default);

        combined.IsSuccess.Should().BeFalse();
        combined.IsFailure.Should().BeTrue();
        combined.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public void Combine_accepts_a_sequence_as_well_as_an_argument_list()
    {
        IEnumerable<Result> results = [Result.Success(), Error.Validation("name", "Name is required.")];

        Result.Combine(results).Errors.Select(e => e.Code).Should().Equal("name");
        Result.Combine([Result.Success(), Result.Success()]).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Combine_of_values_produces_all_of_them_or_all_the_reasons_it_cannot()
    {
        Result.Combine(Result<int>.Success(1), Result<int>.Success(2))
            .Value.Should().Equal(1, 2);

        Result.Combine(Result<int>.Success(1), Error.Validation("two", "Not a number."))
            .Errors.Select(e => e.Code).Should().Equal("two");
    }

    [Fact]
    public void TryGetError_reads_the_failure_the_way_TryGetValue_reads_the_value()
    {
        Result<int> failure = Error.NotFound("thing.missing", "No such thing.");

        failure.TryGetError(out var error).Should().BeTrue();
        error.Code.Should().Be("thing.missing");

        Result<int> success = 7;

        success.TryGetError(out var none).Should().BeFalse();
        none.IsNone.Should().BeTrue();

        Result.Failure(error).TryGetError(out var untyped).Should().BeTrue();
        untyped.Code.Should().Be("thing.missing");
    }

    [Fact]
    public void A_failure_can_be_carried_into_another_result_type_without_naming_its_errors()
    {
        Result<int> failure = Error.Conflict("order.closed", "This order is already closed.");

        var carried = failure.Cast<string>();

        carried.IsFailure.Should().BeTrue();
        carried.Errors.Should().Equal(failure.Errors);

        Result.Failure(Error.Forbidden("nope", "Not allowed.")).Cast<int>().Error.Code.Should().Be("nope");
    }

    [Fact]
    public void Carrying_a_success_into_another_type_is_a_mistake_worth_saying_out_loud()
    {
        var carry = () => Result<int>.Success(1).Cast<string>();

        carry.Should().Throw<InvalidOperationException>().WithMessage("*succeeded*");
    }

    [Fact]
    public void Reasons_convert_back_into_a_failure_so_a_method_can_return_them()
    {
        // R11: what docs/rules/ZERO101.md has always shown as the fix.
        static Result<string> Describe(Result<int> result)
        {
            if (result.IsFailure) return result.Errors;

            return result.Value.ToString();
        }

        Describe(Error.NotFound("thing.missing", "No such thing.")).Error.Code.Should().Be("thing.missing");
        Describe(4).Value.Should().Be("4");
    }
}
