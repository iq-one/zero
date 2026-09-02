using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero;

namespace IQOne.Zero.Events;

/// <summary>Marker the generator uses to find subscribers. Do not implement it directly.</summary>
public interface IEventHandler : IScoped;

/// <summary>
/// Reacts to one event type.
/// </summary>
/// <remarks>
/// <para>
/// Any number of these may exist for one event, which is the whole difference between an
/// event and a command. Each is independent: it must not assume another subscriber ran
/// first, ran at all, or succeeded, because the order subscribers run in is not defined and
/// a failing subscriber does not stop the others.
/// </para>
/// <para>
/// One class may implement this interface several times, for several events. That is the
/// shape to use when one piece of read-model maintenance answers to three facts.
/// </para>
/// <para>
/// The return type is <see cref="Result"/> rather than <see cref="Task"/> so that a
/// subscriber which cannot do its work says so in the value the publisher collects. Nobody
/// reads a subscriber's answer to decide anything — the caller has already committed to the
/// fact — but somebody has to be able to find out that the ledger was not updated.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The event subscribed to.</typeparam>
public interface IEventHandler<in TEvent> : IEventHandler
    where TEvent : IEvent
{
    /// <summary>Reacts to the event.</summary>
    /// <param name="event">What happened.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>
    /// The outcome. A failure is collected and reported to the publisher's caller; it does
    /// not stop the other subscribers and it does not undo the fact.
    /// </returns>
    Task<Result> HandleAsync(TEvent @event, CancellationToken cancellationToken);
}
