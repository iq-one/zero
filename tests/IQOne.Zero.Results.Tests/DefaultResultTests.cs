namespace IQOne.Zero.Results.Tests;

/// <summary>
/// What happens to a result nobody assigned.
/// </summary>
/// <remarks>
/// <para>
/// This is not a hypothetical shape. <c>FirstOrDefault</c> over an empty sequence, a
/// <c>TryGetValue</c> that returned false, a field on a struct that was never set and a
/// <c>default</c> literal in a test all hand you one, and the type's own documentation
/// promises it is a failure rather than a silent success.
/// </para>
/// <para>
/// Every one of these threw <see cref="ArgumentException"/> before, from inside the
/// propagation path — so the failure a caller was trying to pass on became an exception at
/// the line that passed it on. In an HTTP application that is a 500 with a stack trace in
/// place of the mapped problem response.
/// </para>
/// </remarks>
public class DefaultResultTests
{
    [Fact]
    public void Narrowing_one_propagates_the_failure()
    {
        Result narrowed = default(Result<int>);

        narrowed.IsFailure.Should().BeTrue();
        narrowed.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Mapping_one_propagates_the_failure()
    {
        var mapped = default(Result<int>).Map(v => v.ToString());

        mapped.IsFailure.Should().BeTrue();
        mapped.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Binding_one_propagates_the_failure()
    {
        var bound = default(Result<int>).Bind(v => Result<string>.Success("unreachable"));

        bound.IsFailure.Should().BeTrue();
        bound.Errors.Should().ContainSingle();
    }

    [Fact]
    public void The_one_that_comes_out_of_an_empty_sequence_propagates_the_failure()
    {
        // The realistic route in: nobody writes `default(Result<T>)`, they write this.
        var mapped = Array.Empty<Result<int>>().FirstOrDefault().Map(v => v + 1);

        mapped.IsFailure.Should().BeTrue();
        mapped.Error.Code.Should().Be(Error.Uninitialised.Code);
    }

    [Fact]
    public void Its_reason_can_be_read_the_way_every_other_failure_is_read()
    {
        // What IQOne.Zero.Web does with a failure: pick the first error's kind and map it to
        // a status. Against an empty list that was an IndexOutOfRangeException.
        Result<int> unset = default;

        var kinds = unset.Errors.Select(e => e.Kind).Distinct().ToArray();

        kinds.Should().ContainSingle().Which.Should().Be(ErrorKind.Failure);
        unset.Errors[0].Message.Should().NotBeEmpty();
    }

    [Fact]
    public void Combining_one_with_a_success_still_fails()
    {
        Result.Combine(Result.Success(), default(Result<int>)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Reading_its_value_still_says_what_went_wrong()
    {
        var read = () => default(Result<int>).Value;

        read.Should().Throw<InvalidOperationException>().WithMessage("*never initialised*");
    }
}
