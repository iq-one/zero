using IQOne.Zero;

namespace IQOne.Zero.Results.Tests;

/// <summary>
/// Composition is what makes returning failures cheaper than throwing them. If a chain of
/// four steps needs four checks, people go back to exceptions.
/// </summary>
public class ResultCompositionTests
{
    private static readonly Error Boom = Error.Failure("boom", "It went wrong.");

    [Fact]
    public void Map_transforms_the_value_of_a_success()
        => Result<int>.Success(2).Map(v => v * 3).Value.Should().Be(6);

    [Fact]
    public void Map_leaves_a_failure_untouched_and_never_runs_the_transform()
    {
        var ran = false;

        var result = Result<int>.Failure(Boom).Map(v => { ran = true; return v; });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Boom);
        ran.Should().BeFalse();
    }

    [Fact]
    public void Bind_runs_the_next_step_only_after_a_success()
    {
        Result<int>.Success(2).Bind(v => Result<string>.Success($"n={v}")).Value.Should().Be("n=2");

        Result<int>.Failure(Boom).Bind(v => Result<string>.Success("unreachable"))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Ensure_turns_an_unmet_condition_into_a_failure()
    {
        var tooSmall = Error.Validation("amount.small", "The amount must be positive.");

        Result<int>.Success(0).Ensure(v => v > 0, tooSmall).Error.Should().Be(tooSmall);
        Result<int>.Success(5).Ensure(v => v > 0, tooSmall).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Tap_observes_a_success_without_changing_it()
    {
        var seen = 0;

        var result = Result<int>.Success(9).Tap(v => seen = v);

        seen.Should().Be(9);
        result.Value.Should().Be(9);
    }

    [Fact]
    public void TapError_observes_a_failure_without_changing_it()
    {
        var seen = 0;

        var result = Result<int>.Failure(Boom).TapError(errors => seen = errors.Count);

        seen.Should().Be(1);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Match_forces_both_branches_to_exist()
    {
        Result<int>.Success(1).Match(v => "ok", e => "bad").Should().Be("ok");
        Result<int>.Failure(Boom).Match(v => "ok", e => "bad").Should().Be("bad");
    }

    [Fact]
    public void GetValueOr_supplies_a_fallback_instead_of_throwing()
    {
        Result<int>.Failure(Boom).GetValueOr(-1).Should().Be(-1);
        Result<int>.Failure(Boom).GetValueOr(errors => errors.Count).Should().Be(1);
        Result<int>.Success(3).GetValueOr(-1).Should().Be(3);
    }

    [Fact]
    public async Task An_asynchronous_chain_reads_like_the_happy_path()
    {
        var result = await Task.FromResult(Result<int>.Success(2))
            .Map(v => v + 1)
            .Bind(v => Task.FromResult(Result<string>.Success($"n={v}")));

        result.Value.Should().Be("n=3");
    }

    [Fact]
    public async Task An_asynchronous_chain_short_circuits_on_the_first_failure()
    {
        var reached = false;

        var result = await Task.FromResult(Result<int>.Failure(Boom))
            .Bind(v => { reached = true; return Task.FromResult(Result<string>.Success("x")); });

        result.IsFailure.Should().BeTrue();
        reached.Should().BeFalse();
    }
}
