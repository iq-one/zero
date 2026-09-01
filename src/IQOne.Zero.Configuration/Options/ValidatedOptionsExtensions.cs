using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Configuration.Options;

/// Ayarlar BASLANGICTA dogrulanir.
///
/// COMED'de ayarlar statik ConfigurationManager uzerinden, tipsiz ve dogrulamasiz
/// okunuyor: eksik ya da hatali bir ayar uretimde ilk kullanildigi anda ortaya cikiyor.
/// Burada uygulama hatali ayarla ACILMIYOR.
public static class ValidatedOptionsExtensions
{
    /// Konvansiyon: bolum adi TOptions'in tip adidir (RadiologyOptions -> "RadiologyOptions").
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

    /// Ek is kurali dogrulamasi gerekiyorsa.
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
