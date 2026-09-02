using System.Collections.Frozen;

namespace IQOne.Zero.Events;

/// <summary>
/// One row of the generated delivery table.
/// </summary>
/// <remarks>
/// <para>
/// One row per event, not per subscriber. <see cref="Invoke"/> is a closed generic the
/// compiler already produced, so publishing costs a dictionary read and a cast rather than
/// reflection over the event type — and the subscribers it reaches are resolved as
/// <c>IEnumerable&lt;IEventHandler&lt;TEvent&gt;&gt;</c>, which is how the container expresses
/// "as many as there are".
/// </para>
/// <para>
/// <see cref="HandlerTypes"/> is carried for the table's own sake: a host, a test or the
/// <c>zero</c> tool can list who subscribes to what without building a service provider.
/// Delivery does not read it.
/// </para>
/// </remarks>
/// <param name="EventType">The event this row delivers.</param>
/// <param name="HandlerTypes">The subscribers known at compile time, for reporting.</param>
/// <param name="Invoke">Runs the behaviours and then every subscriber.</param>
public sealed record EventEntry(
    Type EventType,
    IReadOnlyList<Type> HandlerTypes,
    Func<IServiceProvider, object, CancellationToken, Task<PublishResult>> Invoke);

/// <summary>Collects delivery entries while modules are being configured.</summary>
public interface IEventRegistryBuilder
{
    /// <summary>
    /// Adds this module's subscribers for one event.
    /// </summary>
    /// <remarks>
    /// Two modules may both subscribe to one event — that is the point of an event — so a
    /// second call for the same event type merges rather than throwing, which is the one
    /// place this table deliberately differs from the request table.
    /// </remarks>
    /// <param name="entry">The event and how to deliver it.</param>
    void Add(EventEntry entry);

    /// <summary>
    /// Records that an event type exists, whether or not this module subscribes to it.
    /// </summary>
    /// <remarks>
    /// The generator emits this from what it saw at compile time, so nothing is scanned. It
    /// is what lets a host find out about an event nobody listens to — which is normal, and
    /// is therefore reported only when asked for.
    /// </remarks>
    /// <param name="eventType">An event type declared in this module.</param>
    void Declare(Type eventType);
}

/// <summary>
/// The delivery table: filled once while modules configure, then frozen and read-only.
/// </summary>
public sealed class EventRegistry : IEventRegistryBuilder
{
    private readonly Dictionary<Type, EventEntry> _entries = [];
    private readonly HashSet<Type> _declared = [];

    private FrozenDictionary<Type, EventEntry>? _frozen;

    /// <inheritdoc />
    public void Add(EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (_frozen is not null)
            throw new InvalidOperationException(
                $"The event table is frozen; '{entry.EventType.Name}' cannot be added. " +
                "Register subscribers while modules are being configured.");

        _declared.Add(entry.EventType);

        if (!_entries.TryGetValue(entry.EventType, out var existing))
        {
            _entries[entry.EventType] = entry;
            return;
        }

        // Both rows close the same generic over the same event type, so either delegate
        // delivers to every subscriber the container holds. The first one is kept and only
        // the roster grows.
        _entries[entry.EventType] = existing with
        {
            HandlerTypes = [.. existing.HandlerTypes.Concat(entry.HandlerTypes).Distinct()]
        };
    }

    /// <inheritdoc />
    public void Declare(Type eventType) => _declared.Add(eventType);

    /// <summary>Seals the table. Called once, after every module has been configured.</summary>
    /// <returns>This instance.</returns>
    public EventRegistry Freeze()
    {
        _frozen = _entries.ToFrozenDictionary();

        return this;
    }

    /// <summary>Finds the delivery row for an event.</summary>
    /// <param name="eventType">The event's concrete type.</param>
    /// <param name="entry">The row, when the event has subscribers.</param>
    /// <returns><see langword="true"/> when at least one subscriber is registered.</returns>
    /// <exception cref="InvalidOperationException">The table has not been frozen yet.</exception>
    public bool TryGet(Type eventType, out EventEntry entry)
    {
        var table = _frozen ?? throw new InvalidOperationException(
            "The event table has not been frozen. Call AddZeroEvents() before AddModules().");

        return table.TryGetValue(eventType, out entry!);
    }

    /// <summary>Every event that has at least one subscriber.</summary>
    public IReadOnlyCollection<EventEntry> Entries => _entries.Values;

    /// <summary>
    /// Event types that exist but that nobody subscribes to.
    /// </summary>
    /// <remarks>
    /// Unlike a request with no handler, this is not by itself a defect: an event is
    /// published whether or not anyone is listening, and requiring a subscriber would put
    /// back exactly the coupling the event removed. It is offered because it is also what a
    /// misspelled subscription looks like, and an application that wants to assert on it in
    /// a test — or refuse to start — now can.
    /// </remarks>
    public IReadOnlyCollection<Type> Unsubscribed => [.. _declared.Where(t => !_entries.ContainsKey(t))];
}
