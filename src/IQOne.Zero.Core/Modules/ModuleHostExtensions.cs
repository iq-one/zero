using IQOne.Zero.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IQOne.Zero.Modules;

/// <summary>Registration and execution of the module lifecycle.</summary>
public static class ModuleHostExtensions
{
    /// <summary>
    /// Orders the modules and runs their configure-services phase, here and now.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The work happens during the call rather than in a step queued for later, because most
    /// applications never run Zero's own <c>Application</c>: an ASP.NET host builds the
    /// provider itself, and a phase queued for a driver that never runs is a phase that never
    /// runs. The symptom was a missing <c>EndpointRegistry</c> at <c>MapZeroEndpoints</c> —
    /// a message pointing nowhere near the cause.
    /// </para>
    /// <para>
    /// Add every capability the modules use before this call: their contributors are what
    /// offer modules a dispatch table or an endpoint table, and they are read here.
    /// Call this once, with every module the application has.
    /// </para>
    /// <para>
    /// Modules whose configure-services step genuinely needs to await something use
    /// <see cref="AddModulesAsync"/> instead.
    /// </para>
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="modules">The modules, in any order.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ModuleDependencyCycleException">The modules' dependencies form a cycle.</exception>
    /// <exception cref="InvalidOperationException">
    /// A module depends on one that was not passed in, or a module's configure-services step
    /// did not complete synchronously.
    /// </exception>
    public static IServiceCollection AddModules(this IServiceCollection services, params IModule[] modules)
    {
        var configuration = Begin(services, modules);

        foreach (var module in configuration.Ordered)
        {
            if (module is not IModuleConfigureServicesStep step) continue;

            var pending = step.OnConfigureServicesAsync(configuration.Context, CancellationToken.None);

            if (pending.IsCompletedSuccessfully) continue;

            // Completed but faulted or cancelled: observing it here rethrows at the call site.
            // Not completed at all: nothing may block, so say what to call instead.
            if (pending.IsCompleted) pending.GetAwaiter().GetResult();
            else
                throw new InvalidOperationException(
                    $"Module '{module.Name}' did not finish configuring services synchronously. " +
                    $"Await {nameof(AddModulesAsync)} instead of calling {nameof(AddModules)}.");
        }

        return End(services, configuration);
    }

    /// <summary>
    /// Orders the modules and runs their configure-services phase, awaiting each one.
    /// </summary>
    /// <remarks>
    /// The asynchronous form of <see cref="AddModules"/>, for a module that genuinely awaits
    /// while it registers. Await it before the provider is built.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="modules">The modules, in any order.</param>
    /// <param name="cancellationToken">Cancels the phase.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <exception cref="ModuleDependencyCycleException">The modules' dependencies form a cycle.</exception>
    /// <exception cref="InvalidOperationException">A module depends on one that was not passed in.</exception>
    public static async ValueTask<IServiceCollection> AddModulesAsync(
        this IServiceCollection services,
        IEnumerable<IModule> modules,
        CancellationToken cancellationToken = default)
    {
        var configuration = Begin(services, modules);

        foreach (var module in configuration.Ordered)
            if (module is IModuleConfigureServicesStep step)
                await step.OnConfigureServicesAsync(configuration.Context, cancellationToken).ConfigureAwait(false);

        return End(services, configuration);
    }

    /// <summary>Human-readable resolved module order, for startup logging and tests.</summary>
    /// <param name="services">The built provider.</param>
    /// <returns>A description of the resolved order.</returns>
    public static string DescribeModuleGraph(this IServiceProvider services)
        => ModuleGraph.Describe(services.GetRequiredService<IReadOnlyList<IModule>>());

    /// <summary>Runs the initialize phase.</summary>
    /// <param name="services">The built provider.</param>
    /// <param name="cancellationToken">Cancels the phase.</param>
    public static ValueTask InitializeModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModuleInitializeStep>(services, static (s, c, t) => s.OnInitializeAsync(c, t), reverse: false, cancellationToken);

    /// <summary>Runs the pre-run phase.</summary>
    /// <param name="services">The built provider.</param>
    /// <param name="cancellationToken">Cancels the phase.</param>
    public static ValueTask PreRunModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModulePreRunStep>(services, static (s, c, t) => s.OnPreRunAsync(c, t), reverse: false, cancellationToken);

    /// <summary>Runs the post-run phase in reverse order.</summary>
    /// <param name="services">The built provider.</param>
    /// <param name="cancellationToken">Cancels the phase.</param>
    public static ValueTask PostRunModulesAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
        => RunAsync<IModulePostRunStep>(services, static (s, c, t) => s.OnPostRunAsync(c, t), reverse: true, cancellationToken);

    /// <summary>What the two entry points share: ordered modules and the context they configure into.</summary>
    private sealed record ModuleConfiguration(
        IReadOnlyList<IModule> Ordered,
        ModuleServiceContext Context,
        IReadOnlyList<IModuleFeatureContributor> Contributors);

    private static ModuleConfiguration Begin(IServiceCollection services, IEnumerable<IModule> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        var ordered = Order(modules);
        var contributors = services.GetRegisteredInstances<IModuleFeatureContributor>();
        var context = new ModuleServiceContext(services);

        foreach (var contributor in contributors)
            contributor.Contribute(context);

        return new ModuleConfiguration(ordered, context, contributors);
    }

    private static IServiceCollection End(IServiceCollection services, ModuleConfiguration configuration)
    {
        foreach (var contributor in configuration.Contributors)
            contributor.Complete(services);

        services.TryAddSingleton(configuration.Ordered);
        services.TryAddSingleton<ModuleLifecycle>();

        // Under a generic host — which is what an ASP.NET application is — nothing else
        // would run the initialize, pre-run and post-run phases, so a module that seeds
        // data on startup would compile, register, and never run.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, ModuleHostedService>());

        return services;
    }

    private static async ValueTask RunAsync<TStep>(
        IServiceProvider services,
        Func<TStep, IModuleContext, CancellationToken, ValueTask> invoke,
        bool reverse,
        CancellationToken cancellationToken)
        where TStep : IModuleStep
    {
        ArgumentNullException.ThrowIfNull(services);

        // An application with no modules is a legitimate application, and it must not fail
        // on the way up or on the way down.
        if (services.GetService<IReadOnlyList<IModule>>() is not { } modules) return;

        var context = new ModuleContext(services);
        var sequence = reverse ? modules.Reverse() : modules;

        foreach (var module in sequence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (module is TStep step) await invoke(step, context, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Topological sort (Kahn), tie-broken by name so the result is deterministic.</summary>
    private static IModule[] Order(IEnumerable<IModule> modules)
    {
        var all = modules.DistinctBy(m => m.GetType()).OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
        var byType = all.ToDictionary(m => m.GetType());

        var pending = all.ToDictionary(m => m, m => Dependencies(m, byType));

        var ordered = new List<IModule>(all.Count);

        while (pending.Count > 0)
        {
            var ready = pending
                .Where(p => p.Value.Count == 0)
                .Select(p => p.Key)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .ToList();

            if (ready.Count == 0) throw new ModuleDependencyCycleException(FindCycle(pending));

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

    /// <summary>
    /// A module's dependencies, resolved against the modules that were actually passed in.
    /// </summary>
    /// <remarks>
    /// A dependency on a module nobody added used to be dropped, so forgetting one silently
    /// degraded the ordering instead of failing. Ordering that cannot be trusted is worse
    /// than ordering that stops the build.
    /// </remarks>
    private static HashSet<IModule> Dependencies(IModule module, Dictionary<Type, IModule> byType)
    {
        var resolved = new HashSet<IModule>();

        foreach (var dependency in module.Dependencies)
        {
            // A module depending on itself stays in the set: it is a cycle of one, and the
            // cycle report is a better answer than quietly ordering it anyway.
            if (byType.TryGetValue(dependency, out var found))
            {
                resolved.Add(found);

                continue;
            }

            throw new InvalidOperationException(
                $"Module '{module.Name}' depends on '{dependency.FullName}', which was not passed to " +
                "AddModules. Add it, or drop the dependency.");
        }

        return resolved;
    }

    /// <summary>
    /// The modules on one cycle, in traversal order.
    /// </summary>
    /// <remarks>
    /// Kahn's algorithm stalls on everything downstream of a cycle too. Reporting all of it
    /// buries the two or three modules a reader has to change, so this walks the remaining
    /// graph until it meets a module already on the path and reports that loop alone.
    /// </remarks>
    private static IReadOnlyList<string> FindCycle(Dictionary<IModule, HashSet<IModule>> pending)
    {
        var path = new List<IModule>();
        var onPath = new HashSet<IModule>();
        var visited = new HashSet<IModule>();

        foreach (var start in pending.Keys.OrderBy(m => m.Name, StringComparer.Ordinal))
            if (Walk(start) is { } cycle)
                return cycle;

        // Unreachable: Kahn only stalls when a cycle exists. Reported in full rather than
        // thrown over, because a wrong-looking report beats no report.
        return [.. pending.Keys.Select(m => m.Name).Order(StringComparer.Ordinal)];

        IReadOnlyList<string>? Walk(IModule module)
        {
            if (onPath.Contains(module))
                return [.. path.Skip(path.IndexOf(module)).Select(m => m.Name), module.Name];

            if (!visited.Add(module)) return null;

            path.Add(module);
            onPath.Add(module);

            foreach (var dependency in pending[module].OrderBy(m => m.Name, StringComparer.Ordinal))
                if (Walk(dependency) is { } cycle)
                    return cycle;

            path.RemoveAt(path.Count - 1);
            onPath.Remove(module);

            return null;
        }
    }
}
