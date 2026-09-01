using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Regify.Dispatch;

/// <summary>Diagnostics reported while generating dispatch and service registrations.</summary>
internal static class Diagnostics
{
    private const string Category = "Regify.Dispatch";

    private static DiagnosticDescriptor Error(string id, string title, string message)
        => new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static DiagnosticDescriptor Warning(string id, string title, string message)
        => new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor HandlerInterfaceMissing = Error(
        "RGF001", "Handler servis arayuzunu uygulamiyor",
        "'{0}' tipi [ServiceMethod] ile isaretli ama IServiceHandler<TRequest, TResponse> uygulamiyor");

    public static readonly DiagnosticDescriptor DuplicateServiceMethod = Error(
        "RGF002", "Ayni servis uclusu iki kez tanimlandi",
        "'{0}/{1}/{2}' uclusu birden fazla handler tarafindan kaydediliyor ('{3}' ve '{4}')");

    public static readonly DiagnosticDescriptor RequestNotServiceRequest = Error(
        "RGF003", "Istek tipi ServiceRequest turevi degil",
        "'{0}' handler'inin istek tipi '{1}', ServiceRequest'ten turemek zorunda");

    public static readonly DiagnosticDescriptor AbstractHandler = Error(
        "RGF004", "Handler somut olmali",
        "'{0}' soyut ya da generic; [ServiceMethod] yalnizca somut, generic olmayan siniflara konur");

    public static readonly DiagnosticDescriptor EmptyRouteSegment = Error(
        "RGF005", "Bos rota bileseni",
        "'{0}' uzerindeki [ServiceMethod] bos bir bilesen iceriyor; module, service ve method dolu olmali");

    public static readonly DiagnosticDescriptor MultipleLifetimes = Error(
        "RGF006", "Birden fazla yasam suresi isareti",
        "'{0}' birden fazla yasam suresi arayuzu uyguluyor ({1}); yalnizca biri olmali");

    public static readonly DiagnosticDescriptor ServiceTypeNotResolved = Error(
        "RGF007", "Servis tipi belirlenemedi",
        "'{0}' icin kaydedilecek arayuz bulunamadi; [ServiceTypes] ile acikca belirtin");

    public static readonly DiagnosticDescriptor RegistrationTargetInvalid = Error(
        "RGF008", "Kaydedilecek tip somut olmali",
        "'{0}' soyut ya da generic; yasam suresi isaretleri yalnizca somut siniflara konur");

    public static readonly DiagnosticDescriptor CaptiveDependency = Error(
        "RGF009", "Captive dependency",
        "Singleton '{0}', daha kisa omurlu '{1}' ({2}) bagimliligini aliyor ve bu bagimlilik ilk cozumde donar");

    public static readonly DiagnosticDescriptor DuplicateRegistration = Warning(
        "RGF010", "Ayni servis tipi iki kez kaydediliyor",
        "'{0}' servis tipi hem '{1}' hem '{2}' tarafindan kaydediliyor; [ServiceTypes] Key ile ayirin");
}
