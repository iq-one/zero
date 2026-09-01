namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>Resolves a route to its handler and runs it.</summary>
public interface IServiceDispatcher
{
    /// <summary>Whether the dispatch table holds an entry for this route.</summary>
    /// <param name="module">First route segment.</param>
    /// <param name="service">Second route segment.</param>
    /// <param name="method">Third route segment.</param>
    /// <returns><see langword="true"/> when the route is registered.</returns>
    bool Exists(string module, string service, string method);

    /// <summary>Runs the handler registered for this route.</summary>
    /// <param name="module">First route segment.</param>
    /// <param name="service">Second route segment.</param>
    /// <param name="method">Third route segment.</param>
    /// <param name="request">
    /// The deserialized request, assignable to the entry's <see cref="ServiceEntry.RequestType"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    /// <returns>The handler's payload, without an envelope.</returns>
    /// <exception cref="Exceptions.DataNotFoundException">No entry matches the route.</exception>
    Task<object?> ExecuteAsync(
        string module, string service, string method, object request, CancellationToken cancellationToken);
}
