namespace IQOne.Zero.Testing.Tests;

/// <summary>
/// A failing assertion is read far more often than a passing one, so half of these tests are
/// about the message. An assertion helper whose failure says "expected True" has replaced a
/// hand-written check with something worse than nothing.
/// </summary>
public class ResultAssertionTests
{
    private static readonly Error Missing = Error.NotFound("invoice.missing", "No invoice with id 7.");

    private static readonly Error Closed = Error.Conflict("invoice.closed", "That invoice is already closed.");

    [Fact]
    public void ShouldSucceed_hands_back_the_value_so_the_test_can_carry_on()
        => Result<int>.Success(7).ShouldSucceed().Should().Be(7);

    [Fact]
    public void ShouldSucceed_passes_a_successful_result_without_a_value()
        => Result.Success().ShouldSucceed().IsSuccess.Should().BeTrue();

    [Fact]
    public void ShouldSucceed_names_every_error_when_the_result_failed()
    {
        var assert = () => Result<int>.Failure([Missing, Closed]).ShouldSucceed();

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("Expected the result to succeed")
            .And.Contain("failed with 2 errors")
            .And.Contain("NotFound: invoice.missing")
            .And.Contain("No invoice with id 7.")
            .And.Contain("Conflict: invoice.closed")
            .And.Contain("That invoice is already closed.");
    }

    [Fact]
    public void ShouldSucceed_says_so_when_the_result_was_never_initialised()
    {
        var assert = () => default(Result<int>).ShouldSucceed();

        // A default result is a failure, and Result names it rather than leaving the
        // reader to wonder which error nobody set.
        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("result.uninitialised");
    }

    [Fact]
    public void ShouldFail_hands_back_the_reasons()
        => Result<int>.Failure([Missing, Closed]).ShouldFail().Should().HaveCount(2);

    [Fact]
    public void ShouldFail_shows_the_value_when_the_result_succeeded()
    {
        var assert = () => Result<string>.Success("Hello, Zero.").ShouldFail();

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("it succeeded with \"Hello, Zero.\"");
    }

    [Fact]
    public void ShouldFailWith_a_code_hands_back_that_error()
        => Result<int>.Failure([Missing, Closed]).ShouldFailWith("invoice.closed").Should().Be(Closed);

    [Fact]
    public void ShouldFailWith_a_code_lists_the_codes_that_were_there_instead()
    {
        var assert = () => Result<int>.Failure(Missing).ShouldFailWith("invoice.closed");

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("Expected the result to fail with error code 'invoice.closed'")
            .And.Contain("invoice.missing");
    }

    [Fact]
    public void ShouldFailWith_a_code_says_the_result_succeeded_when_it_did()
    {
        var assert = () => Result<int>.Success(7).ShouldFailWith("invoice.closed");

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("but it succeeded with 7");
    }

    [Fact]
    public void ShouldFailWith_a_kind_hands_back_that_error()
        => Result.Failure(Missing).ShouldFailWith(ErrorKind.NotFound).Should().Be(Missing);

    [Fact]
    public void ShouldFailWith_a_kind_names_the_kind_that_was_there_instead()
    {
        var assert = () => Result<int>.Failure(Closed).ShouldFailWith(ErrorKind.NotFound);

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("Expected the result to fail with a NotFound error")
            .And.Contain("Conflict: invoice.closed");
    }

    [Fact]
    public void ShouldFailWithCodes_accepts_the_same_set_in_any_order()
        => Result<int>.Failure([Missing, Closed])
            .ShouldFailWithCodes("invoice.closed", "invoice.missing")
            .Should().HaveCount(2);

    [Fact]
    public void ShouldFailWithCodes_reports_what_is_missing_and_what_was_not_expected()
    {
        var assert = () => Result<int>.Failure(Closed).ShouldFailWithCodes("invoice.missing");

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("Missing: 'invoice.missing'")
            .And.Contain("Unexpected: 'invoice.closed'");
    }

    [Fact]
    public void ShouldFailWithCodes_rejects_a_repeated_code_that_only_happened_once()
    {
        var assert = () => Result<int>.Failure(Closed).ShouldFailWithCodes("invoice.closed", "invoice.closed");

        assert.Should().Throw<ZeroAssertionException>();
    }

    [Fact]
    public void ShouldHaveValue_with_a_predicate_hands_back_the_value()
        => Result<InvoiceModel>.Success(new InvoiceModel(1, 250m))
            .ShouldHaveValue(invoice => invoice.Total > 100m)
            .Id.Should().Be(1);

    [Fact]
    public void ShouldHaveValue_quotes_the_condition_and_the_value_that_failed_it()
    {
        var result = Result<InvoiceModel>.Success(new InvoiceModel(1, 0m));

        var assert = () => result.ShouldHaveValue(invoice => invoice.Total > 100m);

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should()
            .Contain("invoice => invoice.Total > 100m", "the compiler hands us the source text of the condition")
            .And.Contain("Total = 0");
    }

    [Fact]
    public void ShouldHaveValue_with_a_predicate_reports_the_errors_when_the_result_failed()
    {
        var assert = () => Result<InvoiceModel>.Failure(Missing).ShouldHaveValue(invoice => invoice.Total > 100m);

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("invoice.missing").And.Contain("No invoice with id 7.");
    }

    [Fact]
    public void ShouldHaveValue_compares_a_value_and_shows_both_sides()
    {
        var assert = () => Result<string>.Success("actual").ShouldHaveValue("expected");

        assert.Should().Throw<ZeroAssertionException>()
            .Which.Message.Should().Contain("\"expected\"").And.Contain("\"actual\"");
    }

    [Fact]
    public void ShouldHaveValue_passes_when_the_values_are_equal()
        => Result<string>.Success("same").ShouldHaveValue("same").Should().Be("same");
}
