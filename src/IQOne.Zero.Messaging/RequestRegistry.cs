using System.Collections.Frozen;

namespace IQOne.Zero.Messaging;

/// <summary>
/// One row of the generated dispatch table.
/// </summary>
/// <remarks>
/// <see cref="Invoke"/> returns <see cref="object"/> because the table is untyped while the
/// caller is not: it hands back the <c>Task&lt;Result&lt;TResponse&gt;&gt;</c> that the sender
/// casts, so dispatching costs a dictionary read and a cast rather than reflection or a
/// boxed result.
/// </remarks>
/// <param name="RequestType">The request this row dispatches.</param>
/// <param name="ResponseType">What handling it produces.</param>
/// <param name="HandlerType">The handler that serves it.</param>
/// <param name="Invoke">Builds the pipeline and runs it.</param>
public sealed record RequestEntry(
    Type RequestType,
    Type ResponseType,
    Type HandlerType,
    Func<IServiceProvider, object, CancellationToken, object> Invoke);

/// <summary>Collects dispatch entries while modules are being configured.</summary>
public interface IRequestRegistryBuilder
{
    /// <summary>Adds one handler to the table.</summary>
    /// <param name="entry">The handler and how to run it.</param>
    /// <exception cref="InvalidOperationException">The request already has a handler.</exception>
    void Add(RequestEntry entry);

    /// <summary>
    /// Records that a request type exists, whether or not this module handles it.
    /// </summary>
    /// <remarks>
    /// This is what lets startup report a request nobody handles. The generator emits it
    /// from what it saw at compile time, so no assembly is scanned to find out.
    /// </remarks>
    /// <param name="requestType">A request type declared in this module.</param>
    void Declare(Type requestType);
}

/// <summary>
/// The dispatch table: filled once while modules configure, then frozen and read-only.
/// </summary>
public sealed class RequestRegistry : IRequestRegistryBuilder
{
    private readonly Dictionary<Type, RequestEntry> _entries = [];
    private readonly HashSet<Type> _declared = [];

    private FrozenDictionary<Type, RequestEntry>? _frozen;

    /// <inheritdoc />
    public void Add(RequestEntry entry)
    {
        if (_frozen is not null)
            throw new InvalidOperationException(
                $"The request table is frozen; '{entry.RequestType.Name}' cannot be added. " +
                "Register handlers while modules are being configured.");

        if (!_entries.TryAdd(entry.RequestType, entry))
            throw new InvalidOperationException(
                $"'{entry.RequestType.FullName}' has two handlers: " +
                $"'{_entries[entry.RequestType].HandlerType.FullName}' and '{entry.HandlerType.FullName}'. " +
                "A request has exactly one handler; use a pipeline behaviour for shared work.");

        _declared.Add(entry.RequestType);
    }

    /// <inheritdoc />
    public void Declare(Type requestType) => _declared.Add(requestType);

    /// <summary>Seals the table. Called once, after every module has been configured.</summary>
    /// <returns>This instance.</returns>
    public RequestRegistry Freeze()
    {
        _frozen = _entries.ToFrozenDictionary();
        return this;
    }

    /// <summary>Finds the handler for a request.</summary>
    /// <param name="requestType">The request's concrete type.</param>
    /// <param name="entry">The handler, when one is registered.</param>
    /// <returns><see langword="true"/> when a handler is registered.</returns>
    public bool TryGet(Type requestType, out RequestEntry entry)
    {
        var table = _frozen ?? throw new InvalidOperationException(
            "The request table has not been frozen. Call AddZeroMessaging() before AddModules().");

        return table.TryGetValue(requestType, out entry!);
    }

    /// <summary>Every registered handler.</summary>
    public IReadOnlyCollection<RequestEntry> Entries => _entries.Values;

    /// <summary>
    /// Request types that exist but have no handler.
    /// </summary>
    /// <remarks>
    /// Known without scanning: the generator records every request it compiled, and this is
    /// the difference between that set and the handlers registered against it.
    /// </remarks>
    public IReadOnlyCollection<Type> Unhandled => [.. _declared.Where(t => !_entries.ContainsKey(t))];
}
