using IQOne.Zero.DependencyInjection.Descriptors;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Extensions;

/// <summary>
/// Reads registered instances straight from an <see cref="IServiceCollection"/>, before an
/// <see cref="IServiceProvider"/> exists.
/// </summary>
/// <remarks>
/// <para>
/// Startup step discovery depends on this. Only registrations that can be realised without a
/// provider are returned — one made with an instance, and one whose implementation is an
/// <see cref="ISingletonInstance"/>; see
/// <see cref="ServiceDescriptorExtensions.ProduceImplementationInstance"/>.
/// </para>
/// <para>
/// The methods deliberately do not borrow the container's names. <c>GetService</c> on a
/// provider means "resolve this"; here it would mean "read it if it happens to be
/// materialisable already", and the two answers differ for most registrations. Reaching for
/// the familiar name and getting the other behaviour is worse than having to learn one word.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>The last instance registered for <typeparamref name="T"/> that can be read yet.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instance, or <see langword="default"/> when none can be produced.</returns>
    public static T? GetRegisteredInstance<T>(this IServiceCollection services)
        => services.GetRegisteredInstance(typeof(T)) is T instance ? instance : default;

    /// <summary>The last instance registered for <paramref name="serviceType"/> that can be read yet.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type. A generic type matches by its definition.</param>
    /// <returns>The instance, or <see langword="null"/> when none can be produced.</returns>
    public static object? GetRegisteredInstance(this IServiceCollection services, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return services.GetRegisteredInstances(Matching(serviceType)).LastOrDefault(i => i is not null);
    }

    /// <summary>The last instance registered for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="InvalidOperationException">No instance could be produced.</exception>
    public static T GetRequiredRegisteredInstance<T>(this IServiceCollection services)
        => (T)services.GetRequiredRegisteredInstance(typeof(T));

    /// <summary>The last instance registered for <paramref name="serviceType"/>.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="InvalidOperationException">No instance could be produced.</exception>
    public static object GetRequiredRegisteredInstance(this IServiceCollection services, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(serviceType);

        if (services.GetRegisteredInstance(serviceType) is { } instance) return instance;

        // "Not registered" and "registered but not readable yet" are different mistakes with
        // different fixes, and the second one is the common one here.
        throw new InvalidOperationException(services.Any(Matching(serviceType))
            ? $"'{serviceType.Name}' is registered, but nothing can be read from that registration before " +
              "the service provider is built. Register it with an instance, or implement " +
              $"{nameof(ISingletonInstance)}, or resolve it from the provider once it exists."
            : $"No service is registered for '{serviceType.Name}'.");
    }

    /// <summary>Every instance registered for exactly <typeparamref name="T"/> that can be read yet.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IReadOnlyList<T> GetRegisteredInstances<T>(this IServiceCollection services)
        => [.. services.GetRegisteredInstances(typeof(T)).OfType<T>()];

    /// <summary>Every instance registered for exactly <paramref name="serviceType"/> that can be read yet.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IReadOnlyList<object?> GetRegisteredInstances(this IServiceCollection services, Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return services.GetRegisteredInstances(Matching(serviceType));
    }

    /// <summary>
    /// Every instance whose descriptor satisfies <paramref name="predicate"/> and can be read yet.
    /// </summary>
    /// <remarks>
    /// The escape hatch for a search the two typed overloads do not express — for everything
    /// assignable to a base, for example:
    /// <c>services.GetRegisteredInstances(d =&gt; typeof(IStep).IsAssignableFrom(d.ServiceType))</c>.
    /// </remarks>
    /// <param name="services">The registrations to search.</param>
    /// <param name="predicate">The descriptor filter.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IReadOnlyList<object?> GetRegisteredInstances(
        this IServiceCollection services, Func<ServiceDescriptor, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(predicate);

        // Snapshotted first: pinning replaces descriptors, and mutating the collection while
        // enumerating it would throw.
        var matches = services.Where(predicate).ToArray();
        var instances = new List<object?>(matches.Length);

        foreach (var descriptor in matches)
        {
            var instance = descriptor.ProduceImplementationInstance();

            instances.Add(instance);

            if (instance is not null) Pin(services, descriptor, instance);
        }

        return instances;
    }

    /// <summary>
    /// Replaces a realised registration with the instance it produced.
    /// </summary>
    /// <remarks>
    /// Without this the container constructs a second object of the same type, so a startup
    /// step that recorded state while the collection was being read is not the step the
    /// application later resolves.
    /// </remarks>
    private static void Pin(IServiceCollection services, ServiceDescriptor descriptor, object instance)
    {
        // Already an instance registration, or a lifetime an instance cannot express.
        if (descriptor.GetImplementationInstance() is not null) return;
        if (descriptor.Lifetime != ServiceLifetime.Singleton) return;

        var index = services.IndexOf(descriptor);

        if (index < 0) return;

        services[index] = descriptor.IsKeyedService
            ? new ServiceDescriptor(descriptor.ServiceType, descriptor.ServiceKey, instance)
            : new ServiceDescriptor(descriptor.ServiceType, instance);
    }

    /// <summary>Matches a service type, treating a generic type as its open definition.</summary>
    private static Func<ServiceDescriptor, bool> Matching(Type serviceType)
        => serviceType.IsGenericType
            ? d => d.ServiceType.IsGenericType &&
                   d.ServiceType.GetGenericTypeDefinition() == serviceType.GetGenericTypeDefinition()
            : d => d.ServiceType == serviceType;
}
