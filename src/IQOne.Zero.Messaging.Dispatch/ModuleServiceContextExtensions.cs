using IQOne.Zero.Modules;

namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>Reaches the dispatch table from inside a module's configure-services step.</summary>
public static class ModuleServiceContextExtensions
{
    /// <summary>The dispatch table a module registers its service methods into.</summary>
    /// <param name="context">The module's configure-services context.</param>
    /// <returns>The registry builder.</returns>
    /// <exception cref="InvalidOperationException">
    /// Dispatch was not added to the application; call <c>AddServiceDispatch()</c> first.
    /// </exception>
    public static IServiceRegistryBuilder Registry(this IModuleServiceContext context)
        => context.Feature<IServiceRegistryBuilder>();
}
