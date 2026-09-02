using IQOne.Zero.Messaging;
using IQOne.Zero.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace IQOne.Zero.Resilience.Tests;

internal sealed record GetRate(string Pair) : IQuery<decimal>;

internal sealed record BookPayment(Guid PaymentId) : ICommand<Guid>;

internal sealed record BookPaymentSafely(Guid PaymentId) : ICommand<Guid>, IIdempotent;

/// <summary>
/// Retrying on a returned failure rather than on a throw, which is the only reason this
/// package exists: a policy written against exceptions never fires on a Zero failure.
/// </summary>
public class RetryBehaviorTests
{
    private static readonly Error Down = Error.Unavailable("rates.down", "The rate service did not answer.");
    private static readonly Error Rejected = Error.Validation("rates.pair", "That is not a pair.");

    private static async Task<(Result<TResponse> Result, int Attempts)> Run<TRequest, TResponse>(
        TRequest request,
        Func<int, Result<TResponse>> answer,
        Action<ResilienceOptions>? configure = null)
        where TRequest : IRequest<TResponse>
    {
        // No waiting: these assert how many attempts were made and which failures earn one.
        // The wait itself is arithmetic and is tested as such in BackoffTests -- a fake
        // clock fires its timers only when advanced, so a real delay here would simply hang.
        var options = new ResilienceOptions { FirstDelay = TimeSpan.Zero, MaxDelay = TimeSpan.Zero };

        configure?.Invoke(options);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));

        var wrapped = Options.Create(options);
        var behavior = new RetryBehavior<TRequest, TResponse>(
            wrapped, new ConsecutiveFailureBrake(wrapped, time), time);

        var attempts = 0;

        var result = await behavior.HandleAsync(
            request,
            () =>
            {
                attempts++;
                return Task.FromResult(answer(attempts));
            },
            CancellationToken.None);

        return (result, attempts);
    }

    [Fact]
    public async Task A_query_that_reports_unavailable_is_tried_again()
    {
        var (result, attempts) = await Run<GetRate, decimal>(
            new GetRate("EURTRY"),
            attempt => attempt < 3 ? Result<decimal>.Failure(Down) : Result<decimal>.Success(42m));

        result.Value.Should().Be(42m);
        attempts.Should().Be(3,
            "nothing was thrown, so a policy written against exceptions would have recorded the first failure as a success");
    }

    [Fact]
    public async Task A_failure_another_attempt_could_not_change_is_not_retried()
    {
        var (result, attempts) = await Run<GetRate, decimal>(
            new GetRate("nonsense"), _ => Result<decimal>.Failure(Rejected));

        result.IsFailure.Should().BeTrue();
        attempts.Should().Be(1, "the same input fails identically; retrying is pure latency");
    }

    [Fact]
    public async Task A_command_is_not_retried_unless_it_says_the_second_handling_is_safe()
    {
        var (_, attempts) = await Run<BookPayment, Guid>(
            new BookPayment(Guid.NewGuid()), _ => Result<Guid>.Failure(Down));

        attempts.Should().Be(1,
            "being wrong here is a customer charged twice, so the safe case is the default");
    }

    [Fact]
    public async Task A_command_that_declares_itself_idempotent_is_retried()
    {
        var (_, attempts) = await Run<BookPaymentSafely, Guid>(
            new BookPaymentSafely(Guid.NewGuid()), _ => Result<Guid>.Failure(Down));

        attempts.Should().Be(3);
    }

    [Fact]
    public async Task Attempts_stop_at_the_limit()
    {
        var (result, attempts) = await Run<GetRate, decimal>(
            new GetRate("EURTRY"), _ => Result<decimal>.Failure(Down),
            options => options.MaxAttempts = 5);

        result.IsFailure.Should().BeTrue();
        attempts.Should().Be(5);
    }

    [Fact]
    public async Task A_success_costs_no_extra_attempt()
    {
        var (result, attempts) = await Run<GetRate, decimal>(
            new GetRate("EURTRY"), _ => Result<decimal>.Success(1m));

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task Switching_the_capability_off_leaves_the_request_alone()
    {
        var (_, attempts) = await Run<GetRate, decimal>(
            new GetRate("EURTRY"), _ => Result<decimal>.Failure(Down),
            options => options.Enabled = false);

        attempts.Should().Be(1);
    }

    [Fact]
    public async Task A_kind_the_application_added_is_retried()
    {
        var conflict = Error.Conflict("rate.stale", "Someone else wrote first.");

        var (_, attempts) = await Run<GetRate, decimal>(
            new GetRate("EURTRY"), _ => Result<decimal>.Failure(conflict),
            options => options.RetryOn.Add(ErrorKind.Conflict));

        attempts.Should().Be(3, "sometimes worth retrying, sometimes a loop — so it is opt-in");
    }

    [Fact]
    public void The_behaviour_retries_around_the_transaction_not_inside_it()
    {
        ResilienceOrder.Retry.Should().BeLessThan(BehaviorOrder.Transaction,
            "each attempt needs a fresh transaction; retrying inside reuses one that may already be doomed");

        ResilienceOrder.Retry.Should().BeGreaterThan(BehaviorOrder.Caching,
            "a stored answer should short-circuit before anything is retried");
    }
}
