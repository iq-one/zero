using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Events;

/// <summary>
/// Tells every subscriber that something happened, and waits for all of them.
/// </summary>
/// <remarks>
/// <para>
/// Publishing is in-process, sequential and awaited. When <see cref="PublishAsync"/> returns,
/// every subscriber has run to completion in the caller's own scope — which means in the
/// caller's transaction, if one is open. Nothing is queued, nothing is retried, and nothing
/// survives the process. That boundary is deliberate and is the whole of what this capability
/// promises; anything that must outlive the request belongs to <c>IQOne.Zero.Outbox</c>.
/// </para>
/// <para>
/// There is no fire-and-forget overload. Handing the work to a background task would take it
/// out of the transaction, out of the cancellation token, out of the caller's knowledge and,
/// on shutdown, out of existence — while looking from the call site exactly like this method.
/// </para>
/// <para>
/// The lookup is a dictionary read against a table built at compile time, not reflection over
/// the event type.
/// </para>
/// </remarks>
public interface IPublisher : IScoped
{
    /// <summary>Delivers the event to every subscriber and reports what each of them did.</summary>
    /// <typeparam name="TEvent">The event's type. Inferred from the argument.</typeparam>
    /// <param name="event">What happened.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>
    /// Every subscriber's outcome. Succeeds when they all did their work, including when
    /// there were none.
    /// </returns>
    /// <exception cref="PublishDepthExceededException">
    /// Delivery re-entered itself more times than <c>EventOptions.MaxPublishDepth</c> allows,
    /// which almost always means a subscriber publishes an event that leads back to this one.
    /// </exception>
    Task<PublishResult> PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent;
}

/// <summary>
/// Thrown when publishing re-enters itself too many times.
/// </summary>
/// <remarks>
/// <para>
/// A subscriber that publishes an event whose subscribers publish the first one again is a
/// cycle, and without this the process dies of a <c>StackOverflowException</c> — which cannot
/// be caught, produces no log line and, inside a transaction, leaves the database holding
/// locks until the connection is reaped.
/// </para>
/// <para>
/// This escapes the delivery loop untouched instead of being captured like an ordinary
/// subscriber fault. A cycle is a defect in the program's shape, and reporting it as one
/// subscriber's bad day would put it in the wrong place.
/// </para>
/// <para>
/// <c>ZERO500</c> finds the cycles that are visible in one compilation. This is what catches
/// the rest.
/// </para>
/// </remarks>
/// <param name="eventType">The event whose delivery exceeded the limit.</param>
/// <param name="depth">The limit that was exceeded.</param>
public sealed class PublishDepthExceededException(Type eventType, int depth)
    : InvalidOperationException(
        $"Publishing '{eventType?.FullName}' re-entered publishing more than {depth} times. " +
        "A subscriber almost certainly publishes an event that leads back to this one; break the cycle, " +
        "or raise EventOptions.MaxPublishDepth if the chain is genuinely that deep.")
{
    /// <summary>The event whose delivery exceeded the limit.</summary>
    public Type EventType { get; } = eventType;

    /// <summary>The limit that was exceeded.</summary>
    public int Depth { get; } = depth;
}
