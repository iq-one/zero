using System.Security.Claims;

namespace IQOne.Zero.Authorization;

/// <summary>
/// What happens to a request that declares neither <see cref="AuthorizeAttribute"/> nor
/// <see cref="AllowAnonymousAttribute"/>.
/// </summary>
/// <remarks>
/// <see cref="Deny"/> is first so that it is also the value of a
/// <see langword="default"/> instance. An enum whose zero value is the permissive one turns
/// a field nobody set into an open door.
/// </remarks>
public enum MissingAuthorization
{
    /// <summary>
    /// Refuse it. The default, and the only value under which forgetting cannot let anyone in.
    /// </summary>
    Deny,

    /// <summary>
    /// Require an identity and nothing more, as though the request carried a bare <c>[Authorize]</c>.
    /// </summary>
    /// <remarks>
    /// A reasonable setting while an existing application is being annotated: it closes the
    /// public hole immediately and leaves the per-request rules to follow.
    /// </remarks>
    RequireAuthentication,

    /// <summary>
    /// Let it through.
    /// </summary>
    /// <remarks>
    /// For a host where authorization is decided somewhere else entirely, and for nothing
    /// else. Under this setting a request is public because someone did not write an
    /// attribute, which is exactly the failure the other two values exist to prevent.
    /// </remarks>
    Allow
}

/// <summary>
/// How authorization behaves, and what the application's policies are.
/// </summary>
/// <remarks>
/// Sealed at the end of <c>AddZeroAuthorization</c>. Policies are read on every request from
/// several threads, and a permission set that can be edited while the application is serving
/// traffic is one that can be widened by a bug at three in the morning.
/// </remarks>
public sealed class AuthorizationOptions
{
    private readonly Dictionary<string, AuthorizationPolicy> _policies = new(StringComparer.Ordinal);

    private MissingAuthorization _unannotated = MissingAuthorization.Deny;
    private string _roleClaimType = ClaimTypes.Role;
    private bool _frozen;

    /// <summary>
    /// What to do with a request that declares no authorization. Refuses it by default.
    /// </summary>
    /// <remarks>
    /// The default is the safe one, and ZERO450 reports the omission at compile time so this
    /// setting rarely decides anything in practice. Changing it does not silence ZERO450:
    /// this says what an undeclared request <em>does</em>, while the diagnostic says the
    /// decision should be written down either way.
    /// </remarks>
    public MissingAuthorization Unannotated
    {
        get => _unannotated;
        set { EnsureMutable(); _unannotated = value; }
    }

    /// <summary>
    /// The claim that carries roles. Defaults to <see cref="ClaimTypes.Role"/>.
    /// </summary>
    /// <remarks>
    /// Set this to match the token. An OpenID Connect provider usually issues <c>roles</c> or
    /// <c>groups</c>; leaving the default against such a token means every role check quietly
    /// finds nothing, which fails closed but for the wrong reason and is maddening to debug.
    /// </remarks>
    public string RoleClaimType
    {
        get => _roleClaimType;
        set { EnsureMutable(); _roleClaimType = value; }
    }

    /// <summary>Every policy the application declared, by name.</summary>
    public IReadOnlyDictionary<string, AuthorizationPolicy> Policies => _policies;

    /// <summary>
    /// Declares a policy: a name, and everything that must be true for the caller to pass it.
    /// </summary>
    /// <remarks>
    /// Every requirement must hold. A policy that should admit alternatives is one
    /// requirement whose handler knows about the alternatives, not several requirements —
    /// otherwise "or" and "and" would look identical at the call site.
    /// </remarks>
    /// <param name="name">
    /// What <c>[Authorize(Policy = ...)]</c> will say. Compared exactly, including case.
    /// </param>
    /// <param name="requirements">Everything the caller must satisfy. At least one.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException">The name is blank, or there are no requirements.</exception>
    /// <exception cref="InvalidOperationException">The name is taken, or the options are sealed.</exception>
    public AuthorizationOptions AddPolicy(string name, params IAuthorizationRequirement[] requirements)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(requirements);

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A policy needs a name; it is what [Authorize] refers to.", nameof(name));

        // A policy with nothing in it passes everyone, which reads as a policy and behaves as
        // an absence of one. Whoever wrote it meant something; refusing here makes them say what.
        if (requirements.Length == 0)
            throw new ArgumentException(
                $"Policy '{name}' has no requirements, so every authenticated caller would pass it. " +
                "Add a requirement, or use a bare [Authorize] to mean 'anyone signed in'.",
                nameof(requirements));

        if (requirements.Any(requirement => requirement is null))
            throw new ArgumentException($"Policy '{name}' contains a null requirement.", nameof(requirements));

        if (_policies.ContainsKey(name))
            throw new InvalidOperationException(
                $"Policy '{name}' is already declared. Two policies with one name means one of them " +
                "is never applied, and which one depends on registration order.");

        _policies[name] = new AuthorizationPolicy(name, requirements.ToArray());

        return this;
    }

    /// <summary>Checks the settings and seals them. Called once, by <c>AddZeroAuthorization</c>.</summary>
    /// <returns>These options.</returns>
    /// <exception cref="InvalidOperationException">A setting cannot be used as it stands.</exception>
    internal AuthorizationOptions Freeze()
    {
        if (string.IsNullOrWhiteSpace(_roleClaimType))
            throw new InvalidOperationException(
                $"{nameof(AuthorizationOptions)}.{nameof(RoleClaimType)} is blank, so no role could ever " +
                $"match. Set it to the claim your tokens carry roles in, for example \"roles\".");

        _frozen = true;

        return this;
    }

    private void EnsureMutable()
    {
        if (_frozen)
            throw new InvalidOperationException(
                "Authorization is already configured. Policies and settings are fixed once " +
                "AddZeroAuthorization has run, so that what a request is permitted to do cannot " +
                "change while the application is serving it.");
    }
}

/// <summary>
/// A named set of requirements, all of which must hold.
/// </summary>
/// <remarks>
/// Constructed only through <see cref="AuthorizationOptions.AddPolicy"/>, so a policy that
/// exists has been through the checks that <c>AddPolicy</c> makes.
/// </remarks>
public sealed class AuthorizationPolicy
{
    internal AuthorizationPolicy(string name, IReadOnlyList<IAuthorizationRequirement> requirements)
    {
        Name = name;
        Requirements = requirements;
    }

    /// <summary>What <c>[Authorize(Policy = ...)]</c> refers to.</summary>
    public string Name { get; }

    /// <summary>Everything the caller must satisfy. Never empty.</summary>
    public IReadOnlyList<IAuthorizationRequirement> Requirements { get; }
}
