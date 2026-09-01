using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Configuration.Options;

/// <summary>
/// Binds and validates options at startup.
/// </summary>
/// <remarks>
/// Settings read untyped and unvalidated fail at the moment they are first used, which in
/// practice means in production, on the code path nobody exercised. Validating on start
/// moves that failure to a place where it costs a restart instead of an incident.
/// </remarks>
public static class ValidatedOptionsExtensions
{
    /// <summary>
    /// Binds <typeparamref name="TOptions"/>, validates its data annotations, and refuses to
    /// start when they do not hold.
    /// </summary>
    /// <remarks>The section defaults to the type's name: <c>MailOptions</c> reads the "MailOptions" section.</remarks>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="sectionName">Section to bind, when it is not the type name.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services, string? sectionName = null)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName ?? typeof(TOptions).Name)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> and adds a rule that data annotations cannot express.
    /// </summary>
    /// <typeparam name="TOptions">The options type.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="validate">Returns false when the settings are unacceptable.</param>
    /// <param name="failureMessage">What is wrong and what a valid value looks like.</param>
    /// <param name="sectionName">Section to bind, when it is not the type name.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddValidatedOptions<TOptions>(
        this IServiceCollection services,
        Func<TOptions, bool> validate,
        string failureMessage,
        string? sectionName = null)
        where TOptions : class
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName ?? typeof(TOptions).Name)
            .ValidateDataAnnotations()
            .Validate(validate, failureMessage)
            .ValidateOnStart();

        return services;
    }
}
