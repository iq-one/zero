using IQOne.Zero;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Events;

/// <summary>
/// Runs a publish through its behaviours and then through every subscriber.
/// </summary>
/// <remarks>
/// Called from generated code, which supplies the type argument, so nothing here is resolved
/// by reflection. It is public because generated code lives in the consumer's assembly; it is
/// not meant to be called by hand.
/// </remarks>
public static class EventDispatch
{
    /// <summary>Delivers one event to everything registered for it.</summary>
    /// <typeparam name="TEvent">The event's concrete type.</typeparam>
    /// <param name="event">What happened.</param>
    /// <param name="services">The scope subscribers and behaviours resolve from.</param>
    /// <param name="cancellationToken">Cancels the delivery.</param>
    /// <returns>What every subscriber did.</returns>
    public static Task<PublishResult> RunAsync<TEvent>(
        TEvent @event, IServiceProvider services, CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        var options = services.GetRequiredService<EventOptions>();
        var time = services.GetRequiredService<TimeProvider>();

        // Sorted by name, and deliberately not left in registration order: registration order
        // would be module order, so adding an unrelated module could silently change what a
        // subscriber observes. An arbitrary order that a rename disturbs is a better teacher
        // than a stable one nobody promised.
        var handlers = services
            .GetServices<IEventHandler<TEvent>>()
            .OrderBy(h => h.GetType().FullName, StringComparer.Ordinal)
            .ToArray();

        var behaviors = services
            .GetServices<IPublishBehavior<TEvent>>()
            .OrderBy(b => b.Order)
            .ToArray();

        PublishHandlerDelegate next = () => DeliverAsync(@event, handlers, options, time, cancellationToken);

        // Wrapped from the inside out, so the lowest Order ends up outermost.
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var downstream = next;

            next = () => behavior.HandleAsync(@event, downstream, cancellationToken);
        }

        return next();
    }

    /// <summary>
    /// Runs every subscriber in turn and records what each of them did.
    /// </summary>
    /// <remarks>
    /// One after another rather than all at once. Concurrency here would put several
    /// subscribers on one <c>DbContext</c> — which is not thread-safe — inside the caller's
    /// transaction, and would buy nothing back: the caller is waiting for all of them anyway.
    /// </remarks>
    private static async Task<PublishResult> DeliverAsync<TEvent>(
        TEvent @event,
        IEventHandler<TEvent>[] handlers,
        EventOptions options,
        TimeProvider time,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        if (handlers.Length == 0) return new PublishResult(typeof(TEvent), []);

        var outcomes = new List<HandlerOutcome>(handlers.Length);

        foreach (var handler in handlers)
        {
            var started = time.GetTimestamp();

            Result result;
            Exception? thrown = null;

            try
            {
                result = await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // The caller gave up. That is not a subscriber failing, and swallowing it
                // would turn a cancelled request into a successful one.
                throw;
            }
            catch (PublishDepthExceededException)
            {
                // A cycle is a defect in the shape of the program, not one subscriber's bad
                // day. It travels all the way out to whoever published first.
                throw;
            }
            catch (Exception exception)
            {
                // Captured rather than propagated, so the subscribers after this one still
                // run. The exception itself is kept on the outcome: a bug that becomes a
                // value is a bug that stops being reported.
                thrown = exception;

                result = Error.Failure(
                    "event.handler.faulted",
                    $"'{handler.GetType().Name}' threw {exception.GetType().Name} while handling " +
                    $"'{typeof(TEvent).Name}': {exception.Message}");
            }

            outcomes.Add(new HandlerOutcome(handler.GetType(), result, time.GetElapsedTime(started), thrown));

            if (result.IsFailure && options.OnHandlerFailure == HandlerFailure.Stop) break;
        }

        return new PublishResult(typeof(TEvent), outcomes);
    }
}
