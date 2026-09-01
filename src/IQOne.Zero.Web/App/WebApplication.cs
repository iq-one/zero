using IQOne.Zero.App;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Web.App;

/// <summary>Base for web-hosted applications.</summary>
public abstract class WebApplication(IServiceCollection serviceCollection) : Application(serviceCollection)
{
    protected WebApplication() : this(new ServiceCollection()) { }
}
