using IQOne.Zero.App;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Configuration.Extensions;

/// <summary>Reaches configuration from a service collection or a running application.</summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Builds the configuration from any registered builder and registers the result, unless
    /// a configuration is already registered.
    /// </summary>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        var builder = services.GetService<IConfigurationBuilder>() ?? new ConfigurationBuilder();
        services.TryAddSingleton<IConfiguration>(builder.Build());

        return services;
    }

    /// <summary>The configuration registered in this collection, if any.</summary>
    /// <param name="services">The registrations to search.</param>
    /// <returns>The configuration, or <see langword="null"/>.</returns>
    public static IConfiguration? GetConfiguration(this IServiceCollection services)
        => services.GetService<IConfiguration>();

    /// <summary>
    /// The application's configuration, taken from the provider once it exists and from the
    /// service collection before that.
    /// </summary>
    /// <param name="application">The application.</param>
    /// <returns>The configuration, or <see langword="null"/>.</returns>
    public static IConfiguration? GetConfiguration(this IApplication application)
        => application.ServiceProvider?.GetService<IConfiguration>()
           ?? application.ServiceCollection.GetConfiguration();

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to the section named after the type.
    /// </summary>
    /// <remarks>
    /// The convention removes the string that would otherwise be repeated at every call
    /// site and drift from the type when it is renamed.
    /// </remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="prefix">Parent section, when the options live under one.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection Configure<TOptions>(
        this IServiceCollection services, string? prefix = null) where TOptions : class
        => services.Configure<TOptions>(typeof(TOptions).Name, prefix);

    /// <summary>Binds <typeparamref name="TOptions"/> to a named section.</summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="key">The section name.</param>
    /// <param name="prefix">Parent section, when the options live under one.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection Configure<TOptions>(
        this IServiceCollection services, string key, string? prefix = null) where TOptions : class
    {
        var configuration = services.GetConfiguration();

        if (configuration is null) return services;

        var path = string.IsNullOrEmpty(prefix) ? key : $"{prefix}:{key}";

        OptionsConfigurationServiceCollectionExtensions
            .Configure<TOptions>(services, configuration.GetSection(path));

        return services;
    }
}
