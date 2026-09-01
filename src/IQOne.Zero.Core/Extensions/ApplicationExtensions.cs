using IQOne.Zero.App;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Extensions;

public static class ApplicationExtensions
{
    public static IApplication? GetApplication(this IServiceCollection services)
        => DependencyInjection.Extensions.ServiceCollectionExtensions.GetService<IApplication>(services);

    public static T GetRequiredService<T>(this IApplication application) where T : notnull
        => application.ServiceProvider.GetRequiredService<T>();

    public static T? GetService<T>(this IApplication application)
        => application.ServiceProvider.GetService<T>();
}
