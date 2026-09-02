using IQOne.Zero;

namespace IQOne.Zero.Events;

/// <summary>
/// What one subscriber did with one event.
/// </summary>
/// <remarks>
/// The handler type is recorded rather than the instance, so an outcome can be logged,
/// asserted on and compared without keeping a scoped object alive past its scope.
/// </remarks>
/// <param name="HandlerType">The subscriber that ran.</param>
/// <param name="Result">What it reported. A failure here does not undo the event.</param>
/// <param name="Elapsed">How long it took. This is the answer to "which subscriber made my command slow".</param>
/// <param name="Exception">
/// The exception it threw, when it threw one. A throwing subscriber is a bug rather than an
/// expected failure, so the exception is kept whole instead of being flattened into an error.
/// </param>
public sealed record HandlerOutcome(
    Type HandlerType,
    Result Result,
    TimeSpan Elapsed,
    Exception? Exception = null)
{
    /// <summary>Whether the subscriber did its work.</summary>
    public bool IsSuccess => Result.IsSuccess;

    /// <summary>Whether the subscriber failed or threw.</summary>
    public bool IsFailure => Result.IsFailure;

    /// <inheritdoc />
    public override string ToString()
        => $"{HandlerType.Name} {(IsSuccess ? "ok" : Result.Error.ToString())} ({Elapsed.TotalMilliseconds:F1} ms)";
}

/// <summary>
/// What happened when an event was published: every subscriber, and what each of them did.
/// </summary>
/// <remarks>
/// <para>
/// A plain <see cref="Result"/> would say "something failed" and lose which of the five
/// subscribers it was. That is the one thing a caller actually needs, because the caller
/// cannot retry the publish — the fact has already happened — and can only decide what to do
/// about the subscriber that did not keep up.
/// </para>
/// <para>
/// An event with no subscribers succeeds with no outcomes. Publishing into a room with
/// nobody in it is not a failure; see <c>EventOptions.RequireSubscriberForEveryEvent</c> for
/// the case where it should be.
/// </para>
/// </remarks>
public sealed class PublishResult
{
    /// <summary>Records what every subscriber did.</summary>
    /// <param name="eventType">The event that was published.</param>
    /// <param name="outcomes">One entry per subscriber that ran, in the order they ran.</param>
    public PublishResult(Type eventType, IReadOnlyList<HandlerOutcome> outcomes)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(outcomes);

        EventType = eventType;
        Outcomes = outcomes;

        // Computed once: a caller checking IsFailure and then reading Errors would otherwise
        // walk the outcomes twice on every publish, and the failing path is the rarer one.
        Errors = [.. outcomes.Where(o => o.IsFailure).SelectMany(o => o.Result.Errors)];
        IsSuccess = Errors.Count == 0;
    }

    /// <summary>The event that was published.</summary>
    public Type EventType { get; }

    /// <summary>
    /// One entry per subscriber that ran, in the order they ran.
    /// </summary>
    /// <remarks>
    /// That order is an implementation detail and is stated in the rule file as one; it is
    /// recorded here so a failure can be reproduced, not so a subscriber can rely on it.
    /// </remarks>
    public IReadOnlyList<HandlerOutcome> Outcomes { get; }

    /// <summary>Every reason a subscriber gave for not doing its work. Empty when they all did.</summary>
    public IReadOnlyList<Error> Errors { get; }

    /// <summary>Whether every subscriber did its work.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether at least one subscriber failed or threw.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Every exception a subscriber threw. Empty when none did.</summary>
    /// <remarks>
    /// A subscriber that throws is captured rather than propagated, so that the subscribers
    /// after it still run. This is where the captured exceptions are, and ignoring them is
    /// how a bug in a subscriber becomes invisible.
    /// </remarks>
    public IReadOnlyList<Exception> Exceptions => [.. Outcomes.Select(o => o.Exception).OfType<Exception>()];

    /// <summary>Narrows this to the framework's ordinary outcome, keeping every reason.</summary>
    /// <returns>Success when every subscriber succeeded; otherwise a failure carrying all their errors.</returns>
    public Result AsResult() => IsSuccess ? Result.Success() : Result.Failure(Errors);

    /// <summary>Narrows this to the framework's ordinary outcome, so a handler can return it.</summary>
    /// <param name="result">What happened when the event was published.</param>
    public static implicit operator Result(PublishResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.AsResult();
    }

    /// <inheritdoc />
    public override string ToString()
        => $"{EventType.Name}: {Outcomes.Count(o => o.IsSuccess)}/{Outcomes.Count} subscribers succeeded";
}
