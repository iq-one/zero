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

    // ---- the valueless Result -------------------------------------------------------------

    [Fact]
    public void The_valueless_result_composes_too()
    {
        // A command handler returns Result<Unit> and calls things that return Result. Before,
        // every extension took Result<T>, so this whole side of the type had no chain at all.
        var steps = new List<string>();

        var ok = Result.Success()
            .Tap(() => steps.Add("tapped"))
            .TapError(errors => steps.Add("never"))
            .Bind(() => Result.Success());

        ok.IsSuccess.Should().BeTrue();
        steps.Should().Equal("tapped");

        var failed = Result.Failure(Boom)
            .Tap(() => steps.Add("never"))
            .TapError(errors => steps.Add($"seen {errors.Count}"))
            .Bind(() => Result.Success());

        failed.IsFailure.Should().BeTrue();
        steps.Should().Equal("tapped", "seen 1");
    }

    [Fact]
    public void The_valueless_result_can_start_a_chain_that_produces_a_value()
    {
        Result.Success().Bind(() => Result<int>.Success(3)).Value.Should().Be(3);

        var failed = Result.Failure(Boom).Bind(() => Result<int>.Success(3));

        failed.IsFailure.Should().BeTrue();
        failed.Error.Should().Be(Boom);
    }

    [Fact]
    public async Task An_awaited_valueless_result_can_be_matched()
        => (await Task.FromResult(Result.Failure(Boom)).Match(() => "ok", errors => "bad"))
            .Should().Be("bad");

    // ---- asynchronous forms of everything else ---------------------------------------------

    [Fact]
    public async Task Every_step_of_the_documented_chain_works_on_an_awaited_result()
    {
        var observed = 0;

        var result = await Task.FromResult(Result<int>.Success(2))
            .Ensure(v => v > 0, Error.Validation("small", "Must be positive."))
            .Tap(v => observed = v)
            .TapError(errors => observed = -1)
            .Bind(v => Result<int>.Success(v * 10))
            .Map(v => $"n={v}");

        result.Value.Should().Be("n=20");
        observed.Should().Be(2);
    }

    [Fact]
    public async Task Ensure_on_an_awaited_result_turns_an_unmet_condition_into_a_failure()
    {
        var closed = Error.Conflict("invoice.closed", "Already closed.");

        var result = await Task.FromResult(Result<int>.Success(0)).Ensure(v => v > 0, closed);

        result.Error.Should().Be(closed);
    }

    [Fact]
    public async Task GetValueOr_works_on_an_awaited_result()
    {
        (await Task.FromResult(Result<int>.Failure(Boom)).GetValueOr(-1)).Should().Be(-1);
        (await Task.FromResult(Result<int>.Failure(Boom)).GetValueOr(errors => errors.Count)).Should().Be(1);
    }

    [Fact]
    public async Task A_synchronous_step_can_follow_an_asynchronous_one()
    {
        var typed = await Task.FromResult(Result<int>.Success(2)).Bind(v => Result<string>.Success($"n={v}"));

        typed.Value.Should().Be("n=2");

        var untyped = await Task.FromResult(Result<int>.Success(2)).Bind(v => Result.Success());

        untyped.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_asynchronous_step_can_follow_a_synchronous_one()
    {
        var mapped = await Result<int>.Success(2).Map(v => Task.FromResult(v * 2));

        mapped.Value.Should().Be(4);

        var bound = await Result<int>.Success(2).Bind(v => Task.FromResult(Result.Success()));

        bound.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task An_asynchronous_chain_can_end_in_a_result_with_no_value()
    {
        var reached = false;

        var result = await Task.FromResult(Result<int>.Failure(Boom))
            .Bind(v => { reached = true; return Task.FromResult(Result.Success()); });

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Boom);
        reached.Should().BeFalse();
    }

    // ---- re-tagging a failure on its way out ------------------------------------------------

    [Fact]
    public void MapError_rewrites_the_reasons_without_touching_a_success()
    {
        var retagged = Result<int>.Failure(Boom)
            .MapError(e => Error.Unavailable($"upstream.{e.Code}", e.Message));

        retagged.Error.Code.Should().Be("upstream.boom");
        retagged.Error.Kind.Should().Be(ErrorKind.Unavailable);

        Result<int>.Success(1).MapError(e => Boom).Value.Should().Be(1);

        Result.Failure(Boom).MapError(e => Error.Unavailable("upstream", e.Message))
            .Error.Kind.Should().Be(ErrorKind.Unavailable);
    }

    [Fact]
    public async Task WithError_replaces_a_reason_the_caller_should_not_see()
    {
        var refused = Error.Forbidden("invoice.refused", "You may not see this invoice.");

        Result<int>.Failure(Boom).WithError(refused).Error.Should().Be(refused);
        Result.Failure(Boom).WithError(refused).Error.Should().Be(refused);

        var awaited = await Task.FromResult(Result<int>.Failure(Boom)).WithError(refused);

        awaited.Error.Should().Be(refused);

        var mapped = await Task.FromResult(Result<int>.Failure(Boom)).MapError(e => refused);

        mapped.Error.Should().Be(refused);
    }
}
