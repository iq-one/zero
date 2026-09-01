using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Validation;

/// <summary>Adds request validation to an application.</summary>
public static class ValidationRegistration
{
    /// <summary>
    /// Runs every registered validator before the handler.
    /// </summary>
    /// <remarks>
    /// Validators themselves are registered by the generator: a class implementing
    /// <see cref="IValidator{T}"/> is found at build time, so there is nothing to list here
    /// and nothing to forget.
    /// </remarks>
    /// <param name="services">The registrations to add to.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroValidation(this IServiceCollection services)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        return services;
    }
}
