namespace IQOne.Zero.Authorization;

/// <summary>
/// Declares who may make this request.
/// </summary>
/// <remarks>
/// <para>
/// On the request, not in a mapping table, for the same reason the route is: the two drift
/// apart the moment they live in different places, and a renamed request keeps whatever
/// permission its old name had. Here they cannot disagree.
/// </para>
/// <para>
/// With no <see cref="Policy"/> and no <see cref="Roles"/>, the attribute says only that the
/// caller must be someone. That is a real and common answer — most requests need an identity
/// and nothing more — and writing it down is what distinguishes it from having forgotten.
/// </para>
/// <para>
/// The attribute may appear more than once, and every one of them must pass. Roles within a
/// single attribute are alternatives: <c>[Authorize(Roles = "admin,auditor")]</c> admits
/// either. Two attributes are the way to say "admin, and also in the finance policy".
/// </para>
/// <para>
/// This attribute is what the pipeline reads, in every host. It is not the same thing as the
/// <c>Policy</c> property on a route attribute, which configures the HTTP endpoint; see the
/// package's rule file.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute
{
    /// <summary>The caller must be authenticated, and nothing more.</summary>
    public AuthorizeAttribute() { }

    /// <summary>The caller must be authenticated and satisfy a named policy.</summary>
    /// <param name="policy">The policy, as it was named in <see cref="AuthorizationOptions.AddPolicy"/>.</param>
    public AuthorizeAttribute(string policy) => Policy = policy;

    /// <summary>
    /// The policy the caller must satisfy, or null to require only that they are someone.
    /// </summary>
    /// <remarks>
    /// A policy that was never added refuses every caller. It is a configuration mistake, and
    /// the safe reading of a rule nobody wrote is that nobody passes it.
    /// </remarks>
    public string? Policy { get; set; }

    /// <summary>
    /// Roles the caller may hold, separated by commas. Any one of them is enough.
    /// </summary>
    /// <remarks>
    /// Roles are the shortcut, not the destination. A rule with any shape to it — ownership,
    /// tenancy, an amount limit — is a requirement in a policy, where it can be tested.
    /// </remarks>
    public string? Roles { get; set; }
}

/// <summary>
/// Declares that this request may be made by anyone, including nobody.
/// </summary>
/// <remarks>
/// Required on a public request, because a request that declares nothing is refused. Saying
/// "anyone may do this" out loud is cheap; discovering that a request was public because
/// nobody thought about it is not.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute;
