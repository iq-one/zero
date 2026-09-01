using IQOne.Zero.DependencyInjection.Descriptors;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Extensions;

/// <summary>Reads what a <see cref="ServiceDescriptor"/> will produce, before it produces it.</summary>
public static class ServiceDescriptorExtensions
{
    /// <summary>
    /// The concrete type the descriptor will construct, whether it was registered by type,
    /// by instance or by factory.
    /// </summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns>The implementation type, or <see langword="null"/> when it cannot be determined.</returns>
    public static Type? GetImplementationType(this ServiceDescriptor? descriptor)
    {
        if (descriptor is null) return null;

        return descriptor.ServiceKey is not null
            ? descriptor.KeyedImplementationType
              ?? descriptor.KeyedImplementationInstance?.GetType()
              ?? descriptor.KeyedImplementationFactory?.GetType().GenericTypeArguments.ElementAtOrDefault(2)
            : descriptor.ImplementationType
              ?? descriptor.ImplementationInstance?.GetType()
              ?? descriptor.ImplementationFactory?.GetType().GenericTypeArguments.ElementAtOrDefault(1);
    }

    /// <summary>The instance the descriptor was registered with, if it holds one.</summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns>The registered instance, or <see langword="null"/>.</returns>
    public static object? GetImplementationInstance(this ServiceDescriptor? descriptor)
        => descriptor is null ? null
            : descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance
            : descriptor.ImplementationInstance;

    /// <summary>
    /// Produces the descriptor's instance without a service provider.
    /// </summary>
    /// <remarks>
    /// Only two registrations can be realised this way: one that already holds an instance,
    /// and one whose implementation is an <see cref="ISingletonInstance"/>. Everything else
    /// needs the provider, which does not exist during the configure-services phase.
    /// </remarks>
    /// <param name="descriptor">The descriptor to realise.</param>
    /// <returns>The instance, or <see langword="null"/> when it cannot be produced yet.</returns>
    public static object? ProduceImplementationInstance(this ServiceDescriptor? descriptor)
    {
        if (descriptor?.GetImplementationInstance() is { } instance) return instance;

        if (typeof(ISingletonInstance).IsAssignableFrom(descriptor?.GetImplementationType()))
            return descriptor?.ImplementationFactory?.Invoke(default!);

        return null;
    }
}
