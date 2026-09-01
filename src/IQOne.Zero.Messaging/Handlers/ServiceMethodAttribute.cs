namespace IQOne.Zero.Messaging.Handlers;

/// <summary>
/// Declares the route a handler serves.
/// </summary>
/// <remarks>
/// The route is stated, never derived from the type name. A published endpoint must not
/// move because someone renamed a class, and a rename is exactly the kind of change nobody
/// expects to alter a wire contract.
/// </remarks>
/// <param name="module">First route segment.</param>
/// <param name="service">Second route segment.</param>
/// <param name="method">Third route segment.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ServiceMethodAttribute(string module, string service, string method) : Attribute
{
    /// <summary>First route segment.</summary>
    public string Module { get; } = module;

    /// <summary>Second route segment.</summary>
    public string Service { get; } = service;

    /// <summary>Third route segment.</summary>
    public string Method { get; } = method;
}
