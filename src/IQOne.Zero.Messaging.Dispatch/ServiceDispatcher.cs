using IQOne.Zero.Messaging.Exceptions;

namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>Looks a route up in the frozen registry and runs its handler.</summary>
/// <param name="registry">The frozen dispatch table.</param>
/// <param name="provider">Scope the handler is resolved from.</param>
public sealed class ServiceDispatcher(ServiceRegistry registry, IServiceProvider provider) : IServiceDispatcher
{
    /// <inheritdoc />
    public bool Exists(string module, string service, string method)
        => registry.TryGet(module, service, method, out _);

    /// <inheritdoc />
    public Task<object?> ExecuteAsync(
        string module, string service, string method, object request, CancellationToken cancellationToken)
    {
        if (!registry.TryGet(module, service, method, out var entry))
            throw new DataNotFoundException($"No service is registered at '{module}/{service}/{method}'.");

        return entry.Invoke(provider, request, cancellationToken);
    }
}
