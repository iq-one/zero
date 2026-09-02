using IQOne.Zero.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Lets a module declare the policies guarding its own requests, then seals them.
/// </summary>
/// <remarks>
/// A module that owns a set of routes owns the rules about who may call them. Without this
/// seam every policy in a modular application would have to be declared in the host — far
/// from the requests it applies to, and in a file that has no reason to know why.
/// </remarks>
/// <param name="options">The settings to offer, and then to seal.</param>
internal sealed class AuthorizationFeatureContributor(AuthorizationOptions options)
    : IModuleFeatureContributor
{
    public void Contribute(IModuleFeatureCollection features) => features.Set(options);

    public void Complete(IServiceCollection services) => options.Freeze();
}

/// <summary>Reaches the authorization settings from inside a module's configure step.</summary>
public static class AuthorizationModuleContextExtensions
{
    /// <summary>
    /// The settings a module may add its policies to.
    /// </summary>
    /// <remarks>
    /// Available only while modules are being configured; they are sealed immediately
    /// afterwards, so a policy added later would be one nothing reads.
    /// </remarks>
    /// <param name="context">The module's configure-services context.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="InvalidOperationException">
    /// Authorization was not added to the application; call <c>AddZeroAuthorization()</c> first.
    /// </exception>
    public static AuthorizationOptions Authorization(this IModuleServiceContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return context.Feature<AuthorizationOptions>();
    }
}
