using Microsoft.Extensions.Configuration;
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
    /// Registers a capability's own options: bound by convention when the application has a
    /// configuration, adjusted by <paramref name="configure"/>, validated at startup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the one shape every Zero capability uses for its <c>AddZeroX(options =&gt; ...)</c>
    /// delegate. Before it there were four: an options object built and never registered, an
    /// <c>AddOptions&lt;T&gt;()</c> with neither binding nor validation, a hand-rolled
    /// <c>Configure</c>, and this package's <see cref="AddValidatedOptions{TOptions}(IServiceCollection,string?)"/>.
    /// An agent generalising from one of them guessed wrong three times out of four.
    /// </para>
    /// <para>
    /// The binding is resolved from the provider rather than read here, so it does not matter
    /// whether the configuration was registered before or after this call. It is skipped when
    /// there is no configuration at all: a capability whose defaults are fine has to work in
    /// an application that configures nothing.
    /// </para>
    /// <para>
    /// <paramref name="configure"/> is applied after the binding, so code wins over settings.
    /// For an application's own settings, where a missing value should stop startup, use
    /// <see cref="AddValidatedOptions{TOptions}(IServiceCollection,string?)"/> instead.
    /// </para>
    /// </remarks>
    /// <typeparam name="TOptions">The options type. The section is named after it.</typeparam>
    /// <param name="services">The registrations to add to.</param>
    /// <param name="configure">Adjusts the options after they are bound.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddZeroOptions<TOptions>(
        this IServiceCollection services, Action<TOptions>? configure = null)
        where TOptions : class
    {
        var builder = services.AddOptions<TOptions>()
            .Configure<IServiceProvider>(static (options, provider) =>
                provider.GetService<IConfiguration>()?.GetSection(typeof(TOptions).Name).Bind(options));

        if (configure is not null) builder.Configure(configure);

        builder.ValidateDataAnnotations().ValidateOnStart();

        return services;
    }

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
