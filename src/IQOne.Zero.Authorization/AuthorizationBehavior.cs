using IQOne.Zero.Messaging;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Refuses a request the caller may not make, before anything reads data.
/// </summary>
/// <remarks>
/// <para>
/// Placed at <see cref="BehaviorOrder.Authorization"/>, outside validation: there is no
/// point telling a caller what is wrong with a request they were never allowed to make, and
/// error messages that describe a resource are themselves a small disclosure. Inside
/// logging, so that a refusal is still recorded.
/// </para>
/// <para>
/// The two refusals are not interchangeable.
/// <see cref="ErrorKind.Unauthorized"/> means the caller could not be identified — signing
/// in might change the answer. <see cref="ErrorKind.Forbidden"/> means they were identified
/// and the answer is still no, and signing in again will not help. Returning the first where
/// the second is true sends people round a login loop; returning the second where the first
/// is true hides a missing token behind what looks like a permissions problem.
/// </para>
/// </remarks>
/// <typeparam name="TRequest">The request checked.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
/// <param name="user">Who is making this request.</param>
/// <param name="options">The application's policies and its answer for undeclared requests.</param>
/// <param name="services">The scope requirement handlers are resolved from.</param>
public sealed class AuthorizationBehavior<TRequest, TResponse>(
    ICurrentUser user, AuthorizationOptions options, IServiceProvider services)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => BehaviorOrder.Authorization;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var declared = RequestAuthorization.For(typeof(TRequest));

        if (declared.IsAnonymous) return await next().ConfigureAwait(false);

        if (!declared.IsDeclared)
        {
            if (options.Unannotated == MissingAuthorization.Allow) return await next().ConfigureAwait(false);

            if (options.Unannotated == MissingAuthorization.Deny)
                return Result<TResponse>.Failure(Refuse(
                    "authorization.undeclared",
                    $"'{typeof(TRequest).Name}' declares neither [Authorize] nor [AllowAnonymous]. " +
                    "A request whose permissions nobody wrote down is refused; see ZERO450."));

            // RequireAuthentication: there is nothing to check beyond having an identity,
            // and the check below is that.
        }

        if (!user.IsAuthenticated) return Result<TResponse>.Failure(AuthorizationErrors.Unauthenticated);

        var refusal = await PolicyEvaluator
            .EvaluateAsync(declared, options, user, services, cancellationToken)
            .ConfigureAwait(false);

        return refusal is null
            ? await next().ConfigureAwait(false)
            : Result<TResponse>.Failure(refusal.Value);
    }

    private Error Refuse(string code, string message)
        => user.IsAuthenticated ? Error.Forbidden(code, message) : Error.Unauthorized(code, message);
}

/// <summary>Refusals that do not come from a requirement.</summary>
internal static class AuthorizationErrors
{
    /// <summary>
    /// Nobody is behind this request.
    /// </summary>
    /// <remarks>
    /// Deliberately says nothing about what would have been required. Telling an
    /// unidentified caller which policy guards a request tells them the request exists.
    /// </remarks>
    public static Error Unauthenticated => Error.Unauthorized(
        "authorization.unauthenticated",
        "The caller could not be identified.");
}
