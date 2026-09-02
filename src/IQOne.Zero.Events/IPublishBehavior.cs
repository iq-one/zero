using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Events;

/// <summary>Delivers the event to the subscribers, or to the rest of the wrappers first.</summary>
/// <returns>What every subscriber did.</returns>
public delegate Task<PublishResult> PublishHandlerDelegate();

/// <summary>
/// Wraps a whole publish of a given event shape.
/// </summary>
/// <remarks>
/// <para>
/// This is not <c>IPipelineBehavior</c> and cannot be: that interface is constrained to
/// <c>IRequest&lt;TResponse&gt;</c>, and making an event satisfy it would make an event
/// sendable through <c>ISender</c> — which demands exactly one handler and is the opposite of
/// what an event is for. So events get their own seam, deliberately narrower.
/// </para>
/// <para>
/// Narrower because almost nothing that wraps a request applies to an event. Validation and
/// authorization have no meaning for a fact that has already happened; caching an event would
/// mean not delivering it; the transaction belongs to whoever published. What is left is
/// observation — a log line, a span, a metric — and one seam is enough for all of it.
/// </para>
/// <para>
/// It wraps the publish, not each subscriber. Per-subscriber timing is already on
/// <see cref="HandlerOutcome.Elapsed"/>, and a wrapper per subscriber would multiply the cost
/// of fan-out by the thing that makes fan-out worth having.
/// </para>
/// <para>
/// A behaviour that does not call <c>next</c> stops the event reaching anyone. That is a
/// legitimate thing to want — a kill switch — and a very easy thing to do by accident.
/// </para>
/// </remarks>
/// <typeparam name="TEvent">The event shape wrapped. Use an open generic to wrap everything.</typeparam>
public interface IPublishBehavior<in TEvent> : IScoped
    where TEvent : IEvent
{
    /// <summary>
    /// Ascending; lower runs further out. Behaviours with equal order run in registration
    /// order, which is not something to rely on.
    /// </summary>
    /// <remarks>
    /// Stated rather than inferred, for the same reason as on a request pipeline: whoever
    /// writes the second behaviour needs to be able to say where it goes without editing the
    /// first one.
    /// </remarks>
    int Order => 0;

    /// <summary>Wraps the delivery.</summary>
    /// <param name="event">What happened.</param>
    /// <param name="next">Delivers the event. Skipping it means nobody hears about the event.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>What every subscriber did.</returns>
    Task<PublishResult> HandleAsync(
        TEvent @event, PublishHandlerDelegate next, CancellationToken cancellationToken);
}
