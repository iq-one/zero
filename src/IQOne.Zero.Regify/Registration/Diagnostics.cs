using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Regify.Registration;

/// <summary>
/// Diagnostics reported while generating dispatch and service registrations.
/// </summary>
/// <remarks>
/// Every message states the fix, not only the violation. A diagnostic is the fastest
/// feedback loop both a person and a coding agent have: one that says what to write instead
/// is acted on immediately, one that only names the rule sends the reader looking for docs.
/// </remarks>
internal static class Diagnostics
{
    private const string Category = "Zero.Registration";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    private static DiagnosticDescriptor Error(string id, string title, string message, string description)
        => new(id, title, message, Category, DiagnosticSeverity.Error, true, description, HelpRoot + id);

    private static DiagnosticDescriptor Warning(string id, string title, string message, string description)
        => new(id, title, message, Category, DiagnosticSeverity.Warning, true, description, HelpRoot + id);

    public static readonly DiagnosticDescriptor MultipleLifetimes = Error(
        "RGF006", "More than one lifetime declared",
        "'{0}' declares several lifetimes ({1}). Keep the one that matches how the type is used and " +
        "remove the others.",
        "Lifetime is carried by the abstraction. Implementing two lifetime markers leaves the registration " +
        "ambiguous, and picking one silently would hide the contradiction.");

    public static readonly DiagnosticDescriptor ServiceTypeNotResolved = Error(
        "RGF007", "Service type could not be determined",
        "No interface was found to register '{0}' under. " +
        "Add the matching interface — 'I{0}' — or state the service types with [ServiceTypes(typeof(...))].",
        "Registration defaults to the interface whose name matches the class. When that interface does not " +
        "exist, the service type has to be stated.");

    public static readonly DiagnosticDescriptor RegistrationTargetInvalid = Error(
        "RGF008", "Registration target must be concrete",
        "'{0}' is abstract or generic and cannot be registered. " +
        "Put the lifetime marker on the concrete class instead of on this type.",
        "Lifetime markers on an abstraction declare the lifetime of its implementations; the abstraction " +
        "itself is never registered.");

    public static readonly DiagnosticDescriptor CaptiveDependency = Error(
        "RGF009", "Captive dependency",
        "Singleton '{0}' takes a shorter-lived '{1}' ({2}), which will be frozen on first resolution. " +
        "Take IServiceScopeFactory and resolve '{1}' inside a scope, or reconsider whether '{0}' is a singleton.",
        "A singleton keeps the first instance it is handed for the lifetime of the process. Every later " +
        "request then reads state belonging to whichever request arrived first.");

    public static readonly DiagnosticDescriptor DuplicateRegistration = Warning(
        "RGF010", "Service type registered by two implementations",
        "Both '{1}' and '{2}' register as '{0}', so resolving '{0}' returns whichever came last. " +
        "Separate them with [ServiceTypes(key, typeof({0}))] and resolve by key.",
        "The container keeps both registrations and returns the last one. If both are wanted, resolve " +
        "IEnumerable<{0}>; if one is wanted, make the choice explicit.");
}
