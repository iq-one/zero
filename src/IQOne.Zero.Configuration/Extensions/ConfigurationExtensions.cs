using IQOne.Zero.App;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Configuration.Extensions;

public static class ConfigurationExtensions
{
    public static IServiceCollection AddConfiguration(this IServiceCollection services)
    {
        var builder = services.GetService<IConfigurationBuilder>() ?? new ConfigurationBuilder();
        services.TryAddSingleton<IConfiguration>(builder.Build());

        return services;
    }

    public static IConfiguration? GetConfiguration(this IServiceCollection services)
        => services.GetService<IConfiguration>();

    public static IConfiguration? GetConfiguration(this IApplication application)
        => application.ServiceProvider?.GetService<IConfiguration>()
           ?? application.ServiceCollection.GetConfiguration();

    /// Konvansiyon: bolum adi TOptions'in tip adidir.
    public static IServiceCollection Configure<TOptions>(
        this IServiceCollection services, string? prefix = null) where TOptions : class
        => services.Configure<TOptions>(typeof(TOptions).Name, prefix);

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
