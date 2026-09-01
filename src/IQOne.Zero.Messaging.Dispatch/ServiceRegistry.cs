using System.Collections.Frozen;

namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>
/// The dispatch table: filled once while modules configure, then frozen and read-only.
/// </summary>
/// <remarks>
/// Freezing is what makes lookup safe to share across requests without a lock. A late
/// registration is a bug, not a feature, so <see cref="Add"/> after freezing is refused.
/// </remarks>
public sealed class ServiceRegistry : IServiceRegistryBuilder
{
    private readonly Dictionary<(string Module, string Service, string Method), ServiceEntry> _entries =
        new(ServiceKeyComparer.Instance);

    private FrozenDictionary<(string, string, string), ServiceEntry>? _frozen;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">
    /// The route is already registered, or the table has been frozen.
    /// </exception>
    public void Add(ServiceEntry entry)
    {
        if (_frozen is not null)
            throw new InvalidOperationException(
                $"The dispatch table is frozen; '{entry.Route}' cannot be added. " +
                "Register service methods while modules are being configured.");

        var key = (entry.Module, entry.Service, entry.Method);

        if (!_entries.TryAdd(key, entry))
            throw new InvalidOperationException(
                $"'{entry.Route}' is registered twice. " +
                $"Existing handler: {_entries[key].HandlerType.FullName}, new handler: {entry.HandlerType.FullName}.");
    }

    /// <summary>Seals the table. Called once, after every module has been configured.</summary>
    /// <returns>This instance.</returns>
    public ServiceRegistry Freeze()
    {
        _frozen = _entries.ToFrozenDictionary(ServiceKeyComparer.Instance);
        return this;
    }

    /// <summary>Finds the entry for a route.</summary>
    /// <param name="module">First route segment.</param>
    /// <param name="service">Second route segment.</param>
    /// <param name="method">Third route segment.</param>
    /// <param name="entry">The entry, when the route is registered.</param>
    /// <returns><see langword="true"/> when the route is registered.</returns>
    /// <exception cref="InvalidOperationException">The table has not been frozen.</exception>
    public bool TryGet(string module, string service, string method, out ServiceEntry entry)
    {
        var table = _frozen ?? throw new InvalidOperationException(
            "The dispatch table has not been frozen. Add the dispatch feature contributor to the application.");

        return table.TryGetValue((module, service, method), out entry!);
    }

    /// <summary>Every registered entry, in registration order.</summary>
    public IReadOnlyCollection<ServiceEntry> Entries => _entries.Values;

    /// <summary>Route segments match case-insensitively.</summary>
    private sealed class ServiceKeyComparer : IEqualityComparer<(string, string, string)>
    {
        public static readonly ServiceKeyComparer Instance = new();

        public bool Equals((string, string, string) x, (string, string, string) y)
            => string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Item3, y.Item3, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string, string) obj)
            => HashCode.Combine(
                obj.Item1.ToLowerInvariant(),
                obj.Item2.ToLowerInvariant(),
                obj.Item3.ToLowerInvariant());
    }
}
