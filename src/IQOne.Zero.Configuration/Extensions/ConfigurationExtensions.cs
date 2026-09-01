using IQOne.Zero.App;
using IQOne.Zero.App.Steps;
using IQOne.Zero.Configuration.Steps;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Configuration.Extensions;

/// <summary>Puts a configuration into the application, and reaches it once it is there.</summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Registers the application's <see cref="IConfiguration"/> and makes options validation
    /// actually run at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ValidateOnStart</c> only registers an <c>IStartupValidator</c>; something has to
    /// resolve it and call it. Microsoft's generic host does, which is why the promise holds
    /// in an ASP.NET application and quietly did not in a Zero-hosted one. This adds the
    /// startup step that closes that gap.
    /// </para>
    /// <para>
    /// A configuration the host already registered is kept and chained onto rather than
    /// replaced, so what the host arranged — <c>appsettings.json</c>, the environment
    /// overlay, the command line — survives, and whatever <paramref name="configure"/> adds
    /// overrides it.
    /// </para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adds configuration sources, in increasing order of precedence.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroConfiguration(
        this IServiceCollection services, Action<IConfigurationBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var existing = services.GetConfiguration();

        if (existing is null || configure is not null)
        {
            var builder = services.GetRegisteredInstance<IConfigurationBuilder>() ?? new ConfigurationBuilder();

            if (existing is not null) builder.AddConfiguration(existing);

            configure?.Invoke(builder);

            services.Replace(ServiceDescriptor.Singleton<IConfiguration>(builder.Build()));
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IApplicationInitializeStep, ValidateOptionsOnStartStep>());

        return services;
    }

    /// <summary>The configuration registered in this collection, if any.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The configuration, or <see langword="null"/>.</returns>
    public static IConfiguration? GetConfiguration(this IServiceCollection services)
        => services.GetRegisteredInstance<IConfiguration>();

    /// <summary>
    /// The application's configuration, taken from the provider once it exists and from the
    /// service collection before that.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The configuration, or <see langword="null"/>.</returns>
    public static IConfiguration? GetConfiguration(this IApplication application)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.ServiceProvider?.GetService<IConfiguration>()
               ?? application.ServiceCollection.GetConfiguration();
    }
}
