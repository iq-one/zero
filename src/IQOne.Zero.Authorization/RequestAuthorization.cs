using System.Collections.Concurrent;

namespace IQOne.Zero.Authorization;

/// <summary>
/// What a request type's attributes add up to, worked out once per type.
/// </summary>
/// <remarks>
/// Attribute lookup is reflection, and the answer never changes for a given type, so it is
/// cached. The alternative — reading attributes on every request — puts reflection on the
/// path of every call the application serves for no benefit at all.
/// </remarks>
internal sealed class RequestAuthorization
{
    private static readonly ConcurrentDictionary<Type, RequestAuthorization> Cache = new();

    /// <summary>Anyone may make it, including nobody.</summary>
    private static readonly RequestAuthorization AnyoneMay = new(true, true, [], []);

    /// <summary>The request says nothing at all; <see cref="AuthorizationOptions.Unannotated"/> decides.</summary>
    private static readonly RequestAuthorization Nothing = new(false, false, [], []);

    private RequestAuthorization(
        bool isAnonymous, bool isDeclared, IReadOnlyList<string> policies, IReadOnlyList<RolesRequirement> roleSets)
    {
        IsAnonymous = isAnonymous;
        IsDeclared = isDeclared;
        Policies = policies;
        RoleSets = roleSets;
    }

    /// <summary>Whether the request carries <see cref="AllowAnonymousAttribute"/>.</summary>
    public bool IsAnonymous { get; }

    /// <summary>Whether the request said anything about authorization at all.</summary>
    public bool IsDeclared { get; }

    /// <summary>Named policies the caller must satisfy, all of them.</summary>
    public IReadOnlyList<string> Policies { get; }

    /// <summary>Role sets the caller must satisfy: all of the sets, any role within one.</summary>
    public IReadOnlyList<RolesRequirement> RoleSets { get; }

    public static RequestAuthorization For(Type requestType) => Cache.GetOrAdd(requestType, static type =>
    {
        // AllowAnonymous wins over Authorize on the same type, as it does everywhere else, so
        // that a reader who knows one framework is not surprised by this one. Writing both is
        // still a mistake, and ZERO451 reports it rather than leaving it to be discovered.
        // Any attribute implementing IAuthorizationDeclaration, not one fixed type. A routed
        // request states its policy on its route attribute; reading only AuthorizeAttribute
        // would mean writing the same thing twice, and two declarations of one fact are two
        // that can disagree.
        var declarations = type
            .GetCustomAttributes(inherit: false)
            .OfType<IAuthorizationDeclaration>()
            .ToArray();

        if (declarations.Any(declaration => declaration.AllowsAnonymous)) return AnyoneMay;

        if (declarations.Length == 0) return Nothing;

        var policies = declarations
            .Select(declaration => declaration.Policy)
            .Where(policy => !string.IsNullOrWhiteSpace(policy))
            .Select(policy => policy!)
            .ToArray();

        var roleSets = declarations
            .Select(declaration => Split(declaration.Roles))
            .Where(roles => roles.Length > 0)
            .Select(roles => new RolesRequirement(roles))
            .ToArray();

        return new RequestAuthorization(false, true, policies, roleSets);
    });

    private static string[] Split(string? roles) => roles is null
        ? []
        : roles.Split(',')
            .Select(role => role.Trim())
            .Where(role => role.Length > 0)
            .ToArray();
}
