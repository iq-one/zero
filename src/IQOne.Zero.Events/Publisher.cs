namespace IQOne.Zero.Events;

/// <summary>Looks an event up in the frozen table and delivers it.</summary>
/// <param name="registry">The frozen delivery table.</param>
/// <param name="options">The delivery limits.</param>
/// <param name="services">The scope subscribers and behaviours resolve from.</param>
internal sealed class Publisher(EventRegistry registry, EventOptions options, IServiceProvider services)
    : IPublisher
{
    /// <summary>
    /// How deep the current asynchronous flow is inside publishing.
    /// </summary>
    /// <remarks>
    /// Static and ambient because the recursion goes through the container: a subscriber
    /// resolves its own publisher from a nested call and would otherwise carry no memory of
    /// the publish it is already inside. An <see cref="AsyncLocal{T}"/> flows into everything
    /// awaited within the publish and into nothing else, so two requests being served at once
    /// never see each other's depth.
    /// </remarks>
    private static readonly AsyncLocal<int> Depth = new();

    /// <inheritdoc />
    public async Task<PublishResult> PublishAsync<TEvent>(
        TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : IEvent
    {
        ArgumentNullException.ThrowIfNull(@event);

        // The concrete type, not TEvent: the caller may well be holding the event through an
        // interface or a base, and it is dispatched by what it is.
        var eventType = @event.GetType();

        var depth = Depth.Value + 1;

        if (depth > options.MaxPublishDepth) throw new PublishDepthExceededException(eventType, options.MaxPublishDepth);

        Depth.Value = depth;

        try
        {
            // Nobody is listening. That is a normal, correct outcome for an event, so it is
            // reported as a success with no subscribers rather than as a missing handler.
            return registry.TryGet(eventType, out var entry)
                ? await entry.Invoke(services, @event, cancellationToken).ConfigureAwait(false)
                : new PublishResult(eventType, []);
        }
        finally
        {
            Depth.Value = depth - 1;
        }
    }
}
