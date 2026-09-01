using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Extensions;

/// <summary>
/// Reads registered instances straight from an <see cref="IServiceCollection"/>, before an
/// <see cref="IServiceProvider"/> exists.
/// </summary>
/// <remarks>
/// Startup step discovery depends on this. Only registrations that already hold an instance
/// can be read; see <see cref="ServiceDescriptorExtensions.ProduceImplementationInstance"/>.
/// This is not a substitute for the container — resolve through the provider once it is built.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>The last instance registered for <typeparamref name="T"/>, if any.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instance, or <see langword="default"/> when none can be produced.</returns>
    public static T? GetService<T>(this IServiceCollection services)
        => services.GetService(typeof(T)) is T instance ? instance : default;

    /// <summary>The last instance registered for <paramref name="serviceType"/>, if any.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type. Open generics match by definition.</param>
    /// <returns>The instance, or <see langword="null"/> when none can be produced.</returns>
    public static object? GetService(this IServiceCollection services, Type serviceType)
    {
        var found = serviceType.IsGenericType
            ? services.GetServices(d => d.ServiceType.IsGenericType &&
                                        d.ServiceType.GetGenericTypeDefinition() == serviceType.GetGenericTypeDefinition())
            : services.GetServices(serviceType);

        return found.LastOrDefault();
    }

    /// <summary>The last instance registered for <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="InvalidOperationException">No instance could be produced.</exception>
    public static T GetRequiredService<T>(this IServiceCollection services)
        => (T)services.GetRequiredService(typeof(T));

    /// <summary>The last instance registered for <paramref name="serviceType"/>.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The instance.</returns>
    /// <exception cref="InvalidOperationException">No instance could be produced.</exception>
    public static object GetRequiredService(this IServiceCollection services, Type serviceType)
        => services.GetService(serviceType)
           ?? throw new InvalidOperationException(
               $"No service is registered for '{serviceType.Name}', or its registration cannot be " +
               "realised before the service provider is built.");

    /// <summary>Every instance registered for exactly <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IEnumerable<T> GetServices<T>(this IServiceCollection services)
        => services.GetServices(typeof(T)).OfType<T>();

    /// <summary>Every instance whose service type is assignable to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The service type or one of its bases.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IEnumerable<T> GetAllServices<T>(this IServiceCollection services)
        => services.GetAllServices(typeof(T)).OfType<T>();

    /// <summary>Every instance registered for exactly <paramref name="serviceType"/>.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IEnumerable<object?> GetServices(this IServiceCollection services, Type serviceType)
        => services.GetServices(d => d.ServiceType == serviceType);

    /// <summary>Every instance whose service type is assignable to <paramref name="serviceType"/>.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="serviceType">The service type or one of its bases.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IEnumerable<object?> GetAllServices(this IServiceCollection services, Type serviceType)
        => services.GetServices(d => serviceType.IsAssignableFrom(d.ServiceType));

    /// <summary>Every instance whose descriptor satisfies <paramref name="predicate"/>.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <param name="predicate">The descriptor filter.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IEnumerable<object?> GetServices(
        this IServiceCollection services, Func<ServiceDescriptor, bool> predicate)
        => services.Where(predicate).Select(d => d.ProduceImplementationInstance());

    /// <summary>Materialized form of <see cref="GetServices{T}(IServiceCollection)"/>.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IReadOnlyCollection<T> GetServiceCollection<T>(this IServiceCollection services)
        => [.. services.GetServices<T>()];

    /// <summary>Materialized form of <see cref="GetAllServices{T}(IServiceCollection)"/>.</summary>
    /// <typeparam name="T">The service type or one of its bases.</typeparam>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The instances that could be produced.</returns>
    public static IReadOnlyCollection<T> GetAllServiceCollection<T>(this IServiceCollection services)
        => [.. services.GetAllServices<T>()];
}
