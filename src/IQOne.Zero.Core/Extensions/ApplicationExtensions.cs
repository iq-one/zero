using IQOne.Zero.App;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Extensions;

/// <summary>Shortcuts between an application and its container.</summary>
public static class ApplicationExtensions
{
    /// <summary>The application registered in this collection, if there is one.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The application, or <see langword="null"/>.</returns>
    public static IApplication? GetApplication(this IServiceCollection services)
        => services.GetRegisteredInstance<IApplication>();

    /// <summary>Resolves a service from the application's provider.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="application">The running application.</param>
    /// <returns>The service.</returns>
    /// <exception cref="InvalidOperationException">The service is not registered.</exception>
    public static T GetRequiredService<T>(this IApplication application) where T : notnull
        => application.ServiceProvider.GetRequiredService<T>();

    /// <summary>Resolves a service from the application's provider, if it is registered.</summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <param name="application">The running application.</param>
    /// <returns>The service, or <see langword="default"/>.</returns>
    public static T? GetService<T>(this IApplication application)
        => application.ServiceProvider.GetService<T>();
}
