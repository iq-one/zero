using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Registration;

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
    private const string Registration = "Zero.Registration";
    private const string Web = "Zero.Web";
    private const string HelpRoot = "https://iqone.solutions/zero/rules/";

    private static DiagnosticDescriptor Error(
        string id, string category, string title, string message, string description)
        => new(id, title, message, category, DiagnosticSeverity.Error, true, description, HelpRoot + id);

    private static DiagnosticDescriptor Warning(
        string id, string category, string title, string message, string description)
        => new(id, title, message, category, DiagnosticSeverity.Warning, true, description, HelpRoot + id);

    public static readonly DiagnosticDescriptor MultipleLifetimes = Error(
        "ZERO006", Registration, "More than one lifetime declared",
        "'{0}' declares several lifetimes ({1}). Keep the one that matches how the type is used and " +
        "remove the others.",
        "Lifetime is carried by the abstraction. Implementing two lifetime markers leaves the registration " +
        "ambiguous, and picking one silently would hide the contradiction.");

    public static readonly DiagnosticDescriptor LifetimeOverridesAbstraction = Error(
        "ZERO011", Registration, "A type contradicts the lifetime its abstraction declares",
        "'{0}' declares {1}, but '{2}' already declares {3}. Callers depend on the abstraction and will " +
        "read {3} from it. Remove the marker from '{0}', change the abstraction, or state the exception " +
        "with [{1}] — the attribute exists for a lifetime no abstraction can express.",
        "Letting the type win silently would make the abstraction lie: a consumer injecting the interface " +
        "reads its lifetime from there and has no way to learn the implementation chose another. Letting " +
        "the abstraction win silently would ignore something the author wrote on purpose. Neither is safe " +
        "to guess, so the contradiction is reported and the author settles it.");

    public static readonly DiagnosticDescriptor ServiceTypeNotResolved = Error(
        "ZERO007", Registration, "Service type could not be determined",
        "No interface was found to register '{0}' under. " +
        "Add the matching interface — 'I{0}' — or state the service types with [ServiceTypes(typeof(...))].",
        "Registration defaults to the interface whose name matches the class. When that interface does not " +
        "exist, the service type has to be stated.");

    public static readonly DiagnosticDescriptor RegistrationTargetInvalid = Error(
        "ZERO008", Registration, "Open generic has no service type it can be registered under",
        "'{0}' is an open generic, so it can only be registered under an interface it passes its own " +
        "type parameters to, in the same order. Give it one, or register it by hand in OnConfigureServices.",
        "An open generic is registered as typeof(IService<>) to typeof(Implementation<>). That is only " +
        "possible when the two have the same shape; a partly closed implementation has no such pair.");

    public static readonly DiagnosticDescriptor CaptiveDependency = Error(
        "ZERO009", Registration, "Captive dependency",
        "Singleton '{0}' takes a shorter-lived '{1}' ({2}), which will be frozen on first resolution. " +
        "Take IServiceScopeFactory and resolve '{1}' inside a scope, or reconsider whether '{0}' is a singleton.",
        "A singleton keeps the first instance it is handed for the lifetime of the process. Every later " +
        "request then reads state belonging to whichever request arrived first.");

    public static readonly DiagnosticDescriptor RouteOnNonRequest = Error(
        "ZERO300", Web, "A route is declared on something that is not a request",
        "'{0}' declares an HTTP route but does not implement ICommand, ICommand<T> or IQuery<T>. " +
        "Make it a request, or remove the route attribute.",
        "A route is served by sending its request through the pipeline. A type that is not a " +
        "request has nothing to send, so the attribute would silently do nothing.");


    public static readonly DiagnosticDescriptor EmptyRoutePattern = Error(
        "ZERO301", Web, "Route pattern is empty",
        "The route on '{0}' has no pattern. Give it one, for example \"/invoices/{{id:int}}\".",
        "An empty pattern maps the endpoint to the application root, which is almost never intended.");

    public static readonly DiagnosticDescriptor UnrecognisedRouteAttribute = Error(
        "ZERO303", Web, "A route attribute names no method",
        "'{0}' derives from RouteAttribute, but not from any of Get, Post, Put, Patch or Delete, so " +
        "there is no HTTP method to read. '{1}' would be reachable at no URL. Derive from whichever " +
        "of the five carries the method you want, and pass the pattern straight through: " +
        "'public sealed class ServiceRouteAttribute(string pattern) : PostAttribute(pattern);'.",
        "Deriving from one of the five IS supported — an application whose routes share a shape says " +
        "it once in its own attribute. What cannot be read is a method passed to RouteAttribute's own " +
        "constructor: the generator sees attribute arguments, not the constructor body that forwards " +
        "them. Deriving from a method-carrying attribute puts the method in the base chain, where it " +
        "can be seen. Without this check the endpoint is simply never mapped, and nothing says so.");

    public static readonly DiagnosticDescriptor DuplicateRegistration = Warning(
        "ZERO010", Registration, "Service type registered by two implementations",
        "Both '{1}' and '{2}' register as '{0}', so resolving '{0}' returns whichever came last. " +
        "Separate them with [ServiceTypes(key, typeof({0}))] and resolve by key.",
        "The container keeps both registrations and returns the last one. If both are wanted, resolve " +
        "IEnumerable<{0}>; if one is wanted, make the choice explicit.");
}
