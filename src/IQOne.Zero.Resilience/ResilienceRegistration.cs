using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Resilience;

/// <summary>Adds retrying to an application.</summary>
public static class ResilienceRegistration
{
    /// <summary>
    /// Retries a request whose <see cref="Result{TValue}"/> says the failure was transient,
    /// as long as handling it twice is safe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One call is enough. Nothing changes for a command until it declares
    /// <see cref="IIdempotent"/>, and nothing changes for anything at all until a handler
    /// starts returning <see cref="ErrorKind.Unavailable"/> — which is what makes this safe
    /// to add before anyone has decided which failures are transient.
    /// </para>
    /// <para>
    /// The clock is registered only if the application has not brought its own, so a host
    /// that already controls time keeps control of it.
    /// </para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts the attempts, the waits, the retryable kinds and the brake.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroResilience(
        this IServiceCollection services, Action<ResilienceOptions>? configure = null)
    {
        var options = services
            .AddOptions<ResilienceOptions>()
            .Validate(
                o => o.MaxAttempts >= 1,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.MaxAttempts)} counts the first try, so " +
                "it must be at least 1. To stop retrying altogether, set Enabled to false.")
            .Validate(
                o => o.FirstDelay > TimeSpan.Zero,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.FirstDelay)} must be greater than zero. " +
                "Retrying with no pause reaches the dependency again before it can have recovered, and turns " +
                "one failing caller into three.")
            .Validate(
                o => o.MaxDelay >= o.FirstDelay,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.MaxDelay)} must not be shorter than " +
                $"{nameof(ResilienceOptions.FirstDelay)}, which would cap the first wait below the value that " +
                "was set for it. Raise the ceiling or lower the first wait.")
            .Validate(
                o => o.BackoffFactor >= 1,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.BackoffFactor)} must be at least 1. " +
                "A factor below 1 shortens each wait, so the harder a dependency is struggling the faster it " +
                "is asked again. Use 1 for a constant wait.")
            .Validate(
                o => o is { Jitter: >= 0 and <= 1 },
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.Jitter)} is the fraction of each wait " +
                "decided at random, so it must be between 0 and 1. Use 0 for exact waits in a test and 0.5 for " +
                "the default.")
            .Validate(
                o => o.RetryOn.Count > 0,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.RetryOn)} is empty, so nothing would " +
                "ever be retried. To stop retrying altogether, set Enabled to false; to retry the usual thing, " +
                $"leave it as {nameof(ErrorKind)}.{nameof(ErrorKind.Unavailable)}.")
            .Validate(
                o => !o.RetryOn.Overlaps(ResilienceOptions.NeverWorthRetrying),
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.RetryOn)} contains a kind that another " +
                "attempt cannot change: the same input fails the same rules, the same caller is still not " +
                "permitted, and the row that was not there is still not there. Remove " +
                string.Join(", ", ResilienceOptions.NeverWorthRetrying) + ".")
            .Validate(
                o => o.PauseRetriesAfterConsecutiveFailures >= 0,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.PauseRetriesAfterConsecutiveFailures)} " +
                "counts failures, so it cannot be negative. Use 0 to let a failing request type go on being " +
                "retried for as long as it keeps failing.")
            .Validate(
                o => o.PauseRetriesAfterConsecutiveFailures == 0 || o.RetryPause > TimeSpan.Zero,
                $"{nameof(ResilienceOptions)}.{nameof(ResilienceOptions.RetryPause)} must be greater than zero " +
                "while the brake is on, or engaging it would release it in the same instant. Set " +
                $"{nameof(ResilienceOptions.PauseRetriesAfterConsecutiveFailures)} to 0 to remove the brake.");

        if (configure is not null) options.Configure(configure);

        options.ValidateOnStart();

        // The application's own clock wins when it has one; otherwise the real one. Waiting
        // goes through TimeProvider so a test can state the backoff it expects rather than
        // sitting through it.
        services.TryAddSingleton(TimeProvider.System);

        // TryAdd, so an application that keys the brake on something other than the request
        // type keeps its own. The default exists to make the package work out of the box.
        services.TryAddSingleton<IRetryBrake, ConsecutiveFailureBrake>();

        // TryAddEnumerable, not Add: a module and a host both being careful would otherwise
        // nest two retriers and turn three attempts into nine.
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>)));

        return services;
    }
}
