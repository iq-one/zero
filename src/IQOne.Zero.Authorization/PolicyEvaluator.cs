using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Runs the requirements a request asks for and reports the first refusal.
/// </summary>
/// <remarks>
/// Every path out of here that is not "allowed" is a refusal. There is no path that treats
/// a missing policy, a missing handler or a handler that threw as permission — that is what
/// "fails closed" means, and it is the whole reason this is one place rather than several.
/// </remarks>
internal static class PolicyEvaluator
{
    /// <summary>Checks everything the request asks for.</summary>
    /// <returns>Null when the caller may proceed; otherwise why not.</returns>
    public static async ValueTask<Error?> EvaluateAsync(
        RequestAuthorization authorization,
        AuthorizationOptions options,
        ICurrentUser user,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        foreach (var roles in authorization.RoleSets)
        {
            var refusal = await CheckAsync(roles, user, services, cancellationToken).ConfigureAwait(false);

            if (refusal is not null) return refusal;
        }

        foreach (var name in authorization.Policies)
        {
            // A policy nobody declared is a deployment mistake, not a permission. Reporting it
            // as a refusal rather than throwing keeps the authorization layer from becoming a
            // source of 500s, and the code says plainly which policy is missing.
            if (!options.Policies.TryGetValue(name, out var policy))
                return Error.Forbidden(
                    "authorization.policy.unknown",
                    $"Policy '{name}' was never declared, so nobody satisfies it. " +
                    "Add it with AuthorizationOptions.AddPolicy, or correct the name on [Authorize].");

            foreach (var requirement in policy.Requirements)
            {
                var refusal = await CheckAsync(requirement, user, services, cancellationToken).ConfigureAwait(false);

                if (refusal is not null) return refusal;
            }
        }

        return null;
    }

    private static async ValueTask<Error?> CheckAsync(
        IAuthorizationRequirement requirement,
        ICurrentUser user,
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        AuthorizationDecision decision;

        try
        {
            decision = await RequirementDispatch
                .CheckAsync(requirement, user, services, cancellationToken)
                .ConfigureAwait(false);
        }
        // Being cancelled is not being refused, so it is left to travel: turning it into a
        // 403 would tell the caller something untrue about their permissions.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Faulted(requirement, exception);
        }

        return decision.IsAllowed ? null : decision.ToError();
    }

    /// <summary>
    /// A check that could not finish did not say yes.
    /// </summary>
    /// <remarks>
    /// Refusing rather than letting the exception out makes the outcome the same whatever
    /// any outer behaviour does with exceptions, and there is nothing to unwind here:
    /// authorization runs outside validation, caching and transactions. The exception itself
    /// is not lost — it rides on the error's metadata, which the transport does not write to
    /// the caller, so a log keeps the detail and the caller learns only that they were refused.
    /// </remarks>
    internal static Error Faulted(IAuthorizationRequirement requirement, Exception exception)
        => Error.Forbidden(
                "authorization.requirement.faulted",
                $"The '{requirement.GetType().Name}' check could not be completed.")
            .With(new Dictionary<string, object?>
            {
                ["requirement"] = requirement.GetType().FullName,
                ["exception"] = exception
            });

    /// <summary>The caller is not known to be permitted, because nothing was there to decide it.</summary>
    internal static AuthorizationDecision Unhandled(Type requirementType)
        => AuthorizationDecision.Deny(
            "authorization.requirement.unhandled",
            $"No handler is registered for '{requirementType.Name}', so it cannot be satisfied.");
}

/// <summary>
/// Finds the handler for a requirement whose type is only known at run time.
/// </summary>
/// <remarks>
/// <para>
/// A policy holds requirements as <see cref="IAuthorizationRequirement"/>, but their handlers
/// are registered under the closed generic <c>IRequirementHandler&lt;TRequirement&gt;</c>,
/// which is the only registration a container can resolve without ambiguity. Bridging the two
/// needs the concrete type, and the concrete type is only known once a requirement is in hand.
/// </para>
/// <para>
/// So it is reflected over exactly once per requirement type, and what is cached is a
/// delegate rather than a <see cref="MethodInfo"/> — after the first request of a given
/// shape, the cost is a dictionary lookup and a call.
/// </para>
/// </remarks>
internal static class RequirementDispatch
{
    internal delegate ValueTask<AuthorizationDecision> Check(
        IAuthorizationRequirement requirement,
        ICurrentUser user,
        IServiceProvider services,
        CancellationToken cancellationToken);

    private static readonly MethodInfo Definition = typeof(RequirementDispatch)
        .GetMethod(nameof(CheckTyped), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly ConcurrentDictionary<Type, Check> Checks = new();

    public static ValueTask<AuthorizationDecision> CheckAsync(
        IAuthorizationRequirement requirement,
        ICurrentUser user,
        IServiceProvider services,
        CancellationToken cancellationToken)
        => For(requirement.GetType())(requirement, user, services, cancellationToken);

    private static Check For(Type requirementType) => Checks.GetOrAdd(
        requirementType,
        static type => (Check)Definition.MakeGenericMethod(type).CreateDelegate(typeof(Check)));

    private static async ValueTask<AuthorizationDecision> CheckTyped<TRequirement>(
        IAuthorizationRequirement requirement,
        ICurrentUser user,
        IServiceProvider services,
        CancellationToken cancellationToken)
        where TRequirement : IAuthorizationRequirement
    {
        // The requirement's own concrete type is what is looked up: a handler registered for
        // a base type does not answer for a derived requirement. Resolving loosely here would
        // mean a rule written for "any document" silently deciding a question about payroll.
        var handler = services.GetService<IRequirementHandler<TRequirement>>();

        if (handler is null) return PolicyEvaluator.Unhandled(typeof(TRequirement));

        return await handler.CheckAsync((TRequirement)requirement, user, cancellationToken).ConfigureAwait(false);
    }
}
