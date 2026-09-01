using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Asks whether the caller may act on one particular thing.
/// </summary>
/// <remarks>
/// <para>
/// Called from a handler, on purpose. "May this caller close <em>this</em> invoice" needs
/// the invoice, and the invoice is loaded inside the handler; a pipeline behaviour that
/// wanted to decide it would have to load it a second time, guess how, and then be wrong
/// about every request whose resource is not a single row.
/// </para>
/// <para>
/// What stays out of the handler is the <em>rule</em>. The handler asks the question; a
/// requirement handler answers it, and can be tested without a handler, a pipeline or a
/// database.
/// </para>
/// </remarks>
public interface IResourceAuthorizer
{
    /// <summary>Decides whether the caller may act on this resource.</summary>
    /// <remarks>
    /// The handler is looked up as <c>IRequirementHandler&lt;TRequirement, TResource&gt;</c>
    /// using the types written at the call site, so pass the resource as the type its handler
    /// was written for rather than as <see cref="object"/> or an interface it happens to
    /// implement.
    /// </remarks>
    /// <typeparam name="TRequirement">What must be true.</typeparam>
    /// <typeparam name="TResource">What it must be true of.</typeparam>
    /// <param name="requirement">The rule to apply.</param>
    /// <param name="resource">The thing being acted on.</param>
    /// <param name="cancellationToken">Cancels a decision that reaches a dependency.</param>
    /// <returns>
    /// Success when the caller may proceed; otherwise a failure that is
    /// <see cref="ErrorKind.Unauthorized"/> when nobody is behind the request and
    /// <see cref="ErrorKind.Forbidden"/> when someone is.
    /// </returns>
    ValueTask<Result> AuthorizeAsync<TRequirement, TResource>(
        TRequirement requirement, TResource resource, CancellationToken cancellationToken)
        where TRequirement : IAuthorizationRequirement;
}

/// <summary>Resolves the handler for a resource requirement and runs it.</summary>
/// <param name="user">Who is asking.</param>
/// <param name="services">The scope the handler is resolved from.</param>
internal sealed class ResourceAuthorizer(ICurrentUser user, IServiceProvider services) : IResourceAuthorizer
{
    public async ValueTask<Result> AuthorizeAsync<TRequirement, TResource>(
        TRequirement requirement, TResource resource, CancellationToken cancellationToken)
        where TRequirement : IAuthorizationRequirement
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (!user.IsAuthenticated) return AuthorizationErrors.Unauthenticated;

        // Both type arguments are known at the call site, so unlike the pipeline's path this
        // needs no reflection at all.
        var handler = services.GetService<IRequirementHandler<TRequirement, TResource>>();

        if (handler is null) return PolicyEvaluator.Unhandled(typeof(TRequirement)).ToError();

        AuthorizationDecision decision;

        try
        {
            decision = await handler
                .CheckAsync(requirement, resource, user, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return PolicyEvaluator.Faulted(requirement, exception);
        }

        return decision.IsAllowed ? Result.Success() : decision.ToError();
    }
}
