namespace IQOne.Zero.Results.Tests;

/// <summary>
/// An error is a value, and a value that does not compare or print like one is not much use:
/// a test cannot assert on it, a set cannot deduplicate it, and a log line does not say what
/// happened.
/// </summary>
public class ErrorTests
{
    private static Dictionary<string, object?> Metadata() => new() { ["invoiceId"] = 7, ["retryable"] = false };

    [Fact]
    public void Two_errors_built_the_same_way_are_the_same_error()
    {
        var first = Error.Conflict("invoice.closed", "Already closed.").With(Metadata());
        var second = Error.Conflict("invoice.closed", "Already closed.").With(Metadata());

        // The generated equality compared the metadata dictionary by reference, so these
        // were not equal and did not hash alike — with the same false answer reaching
        // Result.Equals, HashSet and every Assert.Equal over a result.
        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
        (Result.Failure(first) == Result.Failure(second)).Should().BeTrue();
        new HashSet<Error> { first, second }.Should().ContainSingle();
    }

    [Fact]
    public void Errors_whose_metadata_differs_are_different_errors()
    {
        var seven = Error.Conflict("invoice.closed", "Already closed.").With(new Dictionary<string, object?>
        {
            ["invoiceId"] = 7
        });

        var eight = Error.Conflict("invoice.closed", "Already closed.").With(new Dictionary<string, object?>
        {
            ["invoiceId"] = 8
        });

        seven.Should().NotBe(eight);
    }

    [Fact]
    public void Metadata_is_a_snapshot_so_the_caller_cannot_change_the_error_afterwards()
    {
        var metadata = Metadata();

        var error = Error.Failure("payment.declined", "The bank declined it.").With(metadata);

        metadata["invoiceId"] = 99;
        metadata["added"] = "later";

        error.Metadata!["invoiceId"].Should().Be(7);
        error.Metadata.Should().NotContainKey("added");
    }

    [Fact]
    public void The_absence_of_an_error_says_so_by_name_rather_than_by_kind()
    {
        // Error.None has to have some kind, and the one it has is Failure. IsNone is the
        // question to ask; Kind on a success answers a question nobody asked.
        Error.None.IsNone.Should().BeTrue();
        Error.None.ToString().Should().Be("(none)");
        Result.Success().Error.IsNone.Should().BeTrue();
    }

    [Fact]
    public void A_list_of_reasons_prints_the_reasons()
    {
        // R4: Microsoft.Extensions.Logging calls ToString() on the argument, so both of the
        // documented `LogWarning("... {Errors}", result.Errors)` examples used to produce a
        // log line whose only content was "IQOne.Zero.ErrorList".
        Result<int> result = Result<int>.Failure(
        [
            Error.Validation("name", "Name is required."),
            Error.Validation("email", "Email is not valid.")
        ]);

        var line = $"{result.Errors}";

        line.Should().Contain("name").And.Contain("Name is required.");
        line.Should().Contain("email").And.Contain("Email is not valid.");
        line.Should().NotContain(nameof(ErrorList));
    }

    [Fact]
    public void An_empty_list_of_reasons_says_that_too()
    {
        $"{Result.Success().Errors}".Should().Be("(none)");
    }
}
