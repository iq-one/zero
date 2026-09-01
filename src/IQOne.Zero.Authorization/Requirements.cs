using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Authorization;

/// <summary>
/// One thing that must be true of the caller.
/// </summary>
/// <remarks>
/// <para>
/// A requirement is data: "must hold one of these roles", "must own the invoice", "must be
/// in the same tenant". Deciding whether it holds is a handler's job, which is what makes a
/// rule a class with a test rather than an <c>if</c> buried in a handler.
/// </para>
/// <para>
/// Requirements are constructed once, when policies are declared, and shared by every
/// request afterwards. Keep them immutable and free of dependencies; the handler is where
/// dependencies belong.
/// </para>
/// </remarks>
public interface IAuthorizationRequirement;

/// <summary>Marker the generator uses to find requirement handlers. Do not implement it directly.</summary>
public interface IRequirementHandler : IScoped;

/// <summary>
/// Decides whether a requirement holds for the caller.
/// </summary>
/// <remarks>
/// The caller is passed in rather than injected, so the handler is a function of its inputs
/// and a test can hand it any caller it likes. Dependencies the decision needs — a store, a
/// clock — are constructor parameters as usual.
/// </remarks>
/// <typeparam name="TRequirement">The requirement decided.</typeparam>
public interface IRequirementHandler<in TRequirement> : IRequirementHandler
    where TRequirement : IAuthorizationRequirement
{
    /// <summary>Decides whether the caller satisfies the requirement.</summary>
    /// <param name="requirement">What must be true.</param>
    /// <param name="user">Who is asking. Always authenticated by the time this runs.</param>
    /// <param name="cancellationToken">Cancels a decision that reaches a dependency.</param>
    /// <returns>Whether to allow, and why not when refusing.</returns>
    ValueTask<AuthorizationDecision> CheckAsync(
        TRequirement requirement, ICurrentUser user, CancellationToken cancellationToken);
}

/// <summary>
/// Decides whether a requirement holds for the caller against one particular thing.
/// </summary>
/// <remarks>
/// The resource is what separates "may edit invoices" from "may edit <em>this</em> invoice".
/// It is only known once the invoice has been loaded, so this is asked from inside the
/// handler through <see cref="IResourceAuthorizer"/> rather than from the pipeline. See the
/// package's rule file for why the pipeline cannot do it.
/// </remarks>
/// <typeparam name="TRequirement">The requirement decided.</typeparam>
/// <typeparam name="TResource">What the requirement is decided against.</typeparam>
public interface IRequirementHandler<in TRequirement, in TResource> : IRequirementHandler
    where TRequirement : IAuthorizationRequirement
{
    /// <summary>Decides whether the caller satisfies the requirement for this resource.</summary>
    /// <param name="requirement">What must be true.</param>
    /// <param name="resource">The thing being acted on.</param>
    /// <param name="user">Who is asking. Always authenticated by the time this runs.</param>
    /// <param name="cancellationToken">Cancels a decision that reaches a dependency.</param>
    /// <returns>Whether to allow, and why not when refusing.</returns>
    ValueTask<AuthorizationDecision> CheckAsync(
        TRequirement requirement, TResource resource, ICurrentUser user, CancellationToken cancellationToken);
}

/// <summary>
/// What a handler decided about one requirement.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="default"/> instance is a refusal. That is deliberate, and the same
/// choice <see cref="Result"/> makes: a value that defaults to "allowed" would turn a
/// forgotten assignment, or a struct that came back from somewhere unexpected, into access
/// nobody granted.
/// </para>
/// <para>
/// A refusal carries a code and a message but not a kind. The kind is not the handler's to
/// choose: a requirement only ever runs for a caller who is already known, so every refusal
/// it produces is <see cref="ErrorKind.Forbidden"/>. Letting a handler say otherwise is how
/// a 403 turns into a 401 that invites the caller to sign in again and get nowhere.
/// </para>
/// </remarks>
public readonly record struct AuthorizationDecision
{
    private const string DefaultCode = "authorization.denied";
    private const string DefaultMessage = "The caller is not permitted to do this.";

    private readonly string? _code;
    private readonly string? _message;

    private AuthorizationDecision(bool isAllowed, string? code, string? message)
    {
        IsAllowed = isAllowed;
        _code = code;
        _message = message;
    }

    /// <summary>The caller satisfies the requirement.</summary>
    public static readonly AuthorizationDecision Allowed = new(true, string.Empty, string.Empty);

    /// <summary>Whether the caller satisfies the requirement.</summary>
    public bool IsAllowed { get; }

    /// <summary>Stable identifier for the refusal, conventionally <c>area.reason</c>. Read only when refused.</summary>
    public string Code => _code ?? DefaultCode;

    /// <summary>What the caller would need in order to be allowed. Read only when refused.</summary>
    public string Message => _message ?? DefaultMessage;

    /// <summary>The caller does not satisfy the requirement, for a reason worth stating.</summary>
    /// <param name="code">Stable identifier, conventionally <c>area.reason</c>.</param>
    /// <param name="message">What permission is required.</param>
    /// <returns>The refusal.</returns>
    public static AuthorizationDecision Deny(string code, string message) => new(false, code, message);

    /// <summary>The caller does not satisfy the requirement, without saying more.</summary>
    /// <remarks>Use this where naming the reason would tell the caller something they should not learn.</remarks>
    /// <returns>The refusal.</returns>
    public static AuthorizationDecision Deny() => default;

    /// <summary>The refusal as an error. Always <see cref="ErrorKind.Forbidden"/>; see the type's remarks.</summary>
    internal Error ToError() => Error.Forbidden(Code, Message);
}
