using IQOne.Zero.Modules;
using IQOne.Zero.Modules;
using IQOne.Zero.Data.Context;
using IQOne.Zero.Data.Ownership;
using IQOne.Zero.Data.EntityFramework.Connections;
using IQOne.Zero.Data.Query;
using IQOne.Zero.Data.EntityFramework.Context;
using IQOne.Zero.Data.EntityFramework.Provider;
using IQOne.Zero.Data.EntityFramework.Query;
using IQOne.Zero.Web.Api.Context;
using IQOne.Zero.Web.Api.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using HostWebApplication = Microsoft.AspNetCore.Builder.WebApplication;

namespace IQOne.Zero.Web.Api.App;

/// <summary>
/// Hosts the API. Wraps ASP.NET's <c>WebApplication</c> and implements
/// <see cref="IHost"/>, <see cref="IApplicationBuilder"/> and
/// <see cref="IEndpointRouteBuilder"/> by delegation, so it can be used wherever
/// ASP.NET expects those.
/// </summary>
public class WebApiApplication : Platform.Web.App.WebApplication,
    IHost, IApplicationBuilder, IEndpointRouteBuilder
{
    public WebApiApplication(WebApplicationBuilder hostApplicationBuilder)
        : base(hostApplicationBuilder.Services)
        => HostApplicationBuilder = hostApplicationBuilder;

    public WebApiApplication(string[] args) : this(HostWebApplication.CreateBuilder(args)) { }

    public WebApiApplication() : this([]) { }

    public WebApplicationBuilder HostApplicationBuilder { get; }

    public HostWebApplication? HostApplication { get; protected set; }

    /// <summary>Route prefix and allowed HTTP methods for generated endpoints.</summary>
    public ServiceEndpointOptions EndpointOptions { get; } = new();

    /// <summary>Registers modules explicitly; no assembly scanning takes place.</summary>
    public WebApiApplication AddModules(params IModule[] modules)
    {
        ServiceCollection.AddModules(modules);
        return this;
    }

    protected override Task OnInitializingAsync(CancellationToken cancellationToken)
    {
        // Per-environment secrets file, excluded from source control.
        HostApplicationBuilder.Configuration.AddJsonFile(
            $"appsettings.{HostApplicationBuilder.Environment.EnvironmentName}.Local.json",
            optional: true,
            reloadOnChange: true);

        ServiceCollection.AddSingleton<IConfiguration>(HostApplicationBuilder.Configuration);

        // Registered with TryAdd so a test host can substitute any of them.
        ServiceCollection.AddHttpContextAccessor();
        ServiceCollection.AddOpenApi();

        ServiceCollection.AddOptions<TenantOptions>().BindConfiguration(nameof(TenantOptions));
        ServiceCollection.AddOptions<WriteOwnershipOptions>().BindConfiguration(nameof(WriteOwnershipOptions));

        ServiceCollection.TryAddSingleton<IConnectionStringProvider, ConnectionStringProvider>();
        ServiceCollection.TryAddSingleton<IClock, SystemClock>();
        // Everything bound to a specific data provider comes from this bundle.
        ServiceCollection.AddDataProvider(
            HostApplicationBuilder.Configuration["Data:Provider"] ?? "Ef",
            new EfDataProvider());
        ServiceCollection.TryAddSingleton<IWriteOwnership, DeploymentWriteOwnership>();

        ServiceCollection.TryAddSingleton(provider =>
            provider.GetRequiredService<IOptions<TenantOptions>>().Value);

        ServiceCollection.TryAddScoped<ITenantContext, HttpTenantContext>();
        ServiceCollection.TryAddScoped<ICurrentUser, HttpCurrentUser>();

        return base.OnInitializingAsync(cancellationToken);
    }

    /// <summary>Defers provider construction to ASP.NET's own host builder.</summary>
    protected override Task<IServiceProvider> CreateServiceProviderAsync(CancellationToken cancellationToken)
    {
        HostApplication ??= HostApplicationBuilder.Build();

        return Task.FromResult(HostApplication.Services);
    }

    /// <summary>Runs the lifecycle phases and starts the host.</summary>
    public override async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await BuildAsync(cancellationToken).ConfigureAwait(false);

        await HostApplication!.RunAsync().ConfigureAwait(false);
    }

    /// <summary>Runs the lifecycle phases without starting the host. Intended for tests.</summary>
    public virtual async Task BuildAsync(CancellationToken cancellationToken = default)
    {
        await base.RunAsync(cancellationToken).ConfigureAwait(false);

        ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("IQOne.Zero.Modules")
            .LogInformation("{ModuleGraph}", ServiceProvider.DescribeModuleGraph());

        await ServiceProvider.InitializeModulesAsync(cancellationToken).ConfigureAwait(false);

        await OnMapEndpointsAsync(cancellationToken).ConfigureAwait(false);

        await ServiceProvider.PreRunModulesAsync(cancellationToken).ConfigureAwait(false);
    }

    protected virtual Task OnMapEndpointsAsync(CancellationToken cancellationToken)
    {
        HostApplication!.MapServiceEndpoints(options =>
        {
            options.Prefix = EndpointOptions.Prefix;
            options.HttpMethods = EndpointOptions.HttpMethods;
        });

        // The document is built from the generated endpoints' metadata.
        if (HostApplication!.Environment.IsDevelopment())
        {
            HostApplication.MapOpenApi();
            HostApplication.MapScalarApiReference(options => options.WithTitle("COMED EMR NEXT API"));
        }

        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (ServiceProvider is not null)
            await ServiceProvider.PostRunModulesAsync(cancellationToken).ConfigureAwait(false);

        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    #region IHost

    public IServiceProvider Services => HostApplication!.Services;

    Task IHost.StartAsync(CancellationToken cancellationToken) => HostApplication!.StartAsync(cancellationToken);

    Task IHost.StopAsync(CancellationToken cancellationToken) => HostApplication!.StopAsync(cancellationToken);

    #endregion

    #region IApplicationBuilder

    private IApplicationBuilder ApplicationBuilder => HostApplication!;

    IServiceProvider IApplicationBuilder.ApplicationServices
    {
        get => ApplicationBuilder.ApplicationServices;
        set => ApplicationBuilder.ApplicationServices = value;
    }

    IFeatureCollection IApplicationBuilder.ServerFeatures => ApplicationBuilder.ServerFeatures;

    IDictionary<string, object?> IApplicationBuilder.Properties => ApplicationBuilder.Properties;

    IApplicationBuilder IApplicationBuilder.Use(Func<RequestDelegate, RequestDelegate> middleware)
        => HostApplication!.Use(middleware);

    IApplicationBuilder IApplicationBuilder.New() => ApplicationBuilder.New();

    RequestDelegate IApplicationBuilder.Build() => ApplicationBuilder.Build();

    #endregion

    #region IEndpointRouteBuilder

    private IEndpointRouteBuilder EndpointRouteBuilder => HostApplication!;

    IApplicationBuilder IEndpointRouteBuilder.CreateApplicationBuilder() => EndpointRouteBuilder.CreateApplicationBuilder();

    ICollection<EndpointDataSource> IEndpointRouteBuilder.DataSources => EndpointRouteBuilder.DataSources;

    IServiceProvider IEndpointRouteBuilder.ServiceProvider => EndpointRouteBuilder.ServiceProvider;

    #endregion
}
