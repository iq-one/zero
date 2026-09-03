namespace IQOne.Zero.Authorization;

/// <summary>
/// Something applied to a request that says who may make it.
/// </summary>
/// <remarks>
/// <para>
/// An interface rather than a fixed attribute type, so a declaration that also carries
/// transport detail can be the same declaration. A routed request states its policy on its
/// route attribute; without this the pipeline could not see it, and the author would have to
/// write the same thing twice — where the two can then disagree, and the one the pipeline
/// reads is the one that matters.
/// </para>
/// <para>
/// Implement it on an attribute; nothing else is read.
/// </para>
/// </remarks>
public interface IAuthorizationDeclaration
{
    /// <summary>Named policy the caller must satisfy, or null for "merely authenticated".</summary>
    string? Policy { get; }

    /// <summary>Roles, any one of which will do. Comma-separated, or null.</summary>
    string? Roles { get; }

    /// <summary>Whether the request may be made by anyone, identified or not.</summary>
    bool AllowsAnonymous { get; }
}

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
/// This is what the pipeline reads in a host with no transport of its own — a worker, a
/// consumer, a test. A routed request does not need it: a route attribute is itself an
/// <see cref="IAuthorizationDeclaration"/>, so its <c>Policy</c> is the same declaration and
/// the pipeline reads it directly.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = true)]
public sealed class AuthorizeAttribute : Attribute, IAuthorizationDeclaration
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

    /// <inheritdoc />
    bool IAuthorizationDeclaration.AllowsAnonymous => false;

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
public sealed class AllowAnonymousAttribute : Attribute, IAuthorizationDeclaration
{
    /// <inheritdoc />
    public string? Policy => null;

    /// <inheritdoc />
    public string? Roles => null;

    /// <inheritdoc />
    public bool AllowsAnonymous => true;
}

/// <summary>
/// Marks an attribute type that decides authorization for everything carrying it.
/// </summary>
/// <remarks>
/// <para>
/// ZERO450 asks whether a request says who may make it, and it reads the answer from the
/// attribute's arguments — <c>Policy</c>, <c>Roles</c>, <c>AllowAnonymous</c> — because that
/// is what a reader sees at the request. An attribute that DERIVES the answer instead
/// satisfies the rule without writing anything the analyzer can read:
/// <code>
/// [DeclaresAuthorization]
/// public sealed class ServiceRouteAttribute : PostAttribute
/// {
///     public ServiceRouteAttribute(string pattern) : base(pattern)
///         =&gt; Policy = pattern.TrimStart('/');
/// }
/// </code>
/// </para>
/// <para>
/// The marker is on the ATTRIBUTE TYPE, once, rather than inferred from its constructor.
/// Inferring would work in the assembly that declares it and fail for one referenced as
/// metadata, so the rule would depend on where the attribute lives. Saying it makes the
/// claim reviewable in one place: whoever reads <c>ServiceRouteAttribute</c> sees both that
/// it decides authorization and how.
/// </para>
/// <para>
/// It suppresses nothing else. The attribute still has to supply a policy at runtime —
/// <c>RequestAuthorization</c> reads the live instance — and an attribute that carries this
/// marker while deciding nothing leaves its requests requiring only an authenticated
/// caller, exactly as an empty <c>[Authorize]</c> does.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DeclaresAuthorizationAttribute : Attribute;

