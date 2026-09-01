using IQOne.Zero.App.Steps;
using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Modules;

/// <summary>Registration and execution of the module lifecycle.</summary>
public static class ModuleHostExtensions
{
    /// <summary>
    /// Registers modules as an application step. They run during the application's
    /// configure-services phase.
    /// </summary>
    public static IServiceCollection AddModules(this IServiceCollection services, params IModule[] modules)
    {
        var ordered = Order(modules);

        services.AddSingleton<IReadOnlyList<IModule>>(ordered);
        services.AddSingleton<IApplicationConfigureServicesStep>(new ModuleConfigureServicesStep(ordered));

        return services;
    }

    /// <summary>Runs the configure-services phase. Must complete before the provider is built.</summary>
    public static async ValueTask<IServiceCollection> AddModulesAsync(
        this IServiceCollection services,
        IEnumerable<IModule> modules,
        CancellationToken cancellationToken = default)
    {
        var ordered = Order(modules);
        var contributors = services.GetServiceCollection<IModuleFeatureContributor>();
        var context = new ModuleServiceContext(services);

        foreach (var contributor in contributors)
            contributor.Contribute(context);

        foreach (var module in ordered)
            if (module is IModuleConfigureServicesStep step)
                await step.OnConfigureServicesAsync(context, cancellationToken).ConfigureAwait(false);

        foreach (var contributor in contributors)
            contributor.Complete(services);

        services.TryAddSingleton<IReadOnlyList<IModule>>(ordered);

        return services;
    }

    /// <summary>Human-readable resolved module order, for startup logging and tests.</summary>
    public static string DescribeModuleGraph(this IServiceProvider services)
        => ModuleGraph.Describe(services.GetRequiredService<IReadOnlyList<IModule>>());

    /// <summary>Runs the initialize phase.</summary>
    public static ValueTask InitializeModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModuleInitializeStep>(services, static (s, c, t) => s.OnInitializeAsync(c, t), reverse: false, cancellationToken);

    /// <summary>Runs the pre-run phase.</summary>
    public static ValueTask PreRunModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModulePreRunStep>(services, static (s, c, t) => s.OnPreRunAsync(c, t), reverse: false, cancellationToken);

    /// <summary>Runs the post-run phase in reverse order.</summary>
    public static ValueTask PostRunModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModulePostRunStep>(services, static (s, c, t) => s.OnPostRunAsync(c, t), reverse: true, cancellationToken);

    private static async ValueTask RunAsync<TStep>(
        IServiceProvider services,
        Func<TStep, IModuleContext, CancellationToken, ValueTask> invoke,
        bool reverse,
        CancellationToken cancellationToken)
        where TStep : IModuleStep
    {
        var modules = services.GetRequiredService<IReadOnlyList<IModule>>();
        var context = new ModuleContext(services);
        var sequence = reverse ? modules.Reverse() : modules;

        foreach (var module in sequence)
            if (module is TStep step)
                await invoke(step, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Topological sort (Kahn), tie-broken by name so the result is deterministic.</summary>
    private static IModule[] Order(IEnumerable<IModule> modules)
    {
        var all = modules.DistinctBy(m => m.GetType()).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        var byType = all.ToDictionary(m => m.GetType());

        var pending = all.ToDictionary(
            m => m,
            m => m.Dependencies.Where(byType.ContainsKey).Select(d => byType[d]).ToHashSet());

        var ordered = new List<IModule>(all.Count);

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(p => p.Value.Count == 0)
                .Select(p => p.Key)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0)
                throw new ModuleDependencyCycleException([.. pending.Keys.Select(m => m.Name).Order(StringComparer.Ordinal)]);

            foreach (var module in ready)
            {
                ordered.Add(module);
                pending.Remove(module);
            }

            foreach (var remaining in pending.Values)
                foreach (var module in ready)
                    remaining.Remove(module);
        }

        return [.. ordered];
    }
}
