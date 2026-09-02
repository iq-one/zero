using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IQOne.Zero.Authorization;

/// <summary>
/// Says so, once, when the application never told the framework who its callers are.
/// </summary>
/// <remarks>
/// <para>
/// <c>AddZeroAuthorization</c> registers an anonymous <see cref="ICurrentUser"/> so that a
/// host with no notion of a user still starts. The cost is that forgetting to register a
/// real one looks identical: every protected request is refused, correctly, and the reason —
/// nobody ever said who the caller is — appears nowhere.
/// </para>
/// <para>
/// The framework cannot tell the two apart, because a worker with no users is a legitimate
/// application. So it reports what it sees and how to make the choice explicit, rather than
/// guessing which one this is.
/// </para>
/// <para>
/// The caller's own answer is unchanged: an unidentified caller is still told only that it
/// could not be identified. Telling it which policy guards the request would tell it the
/// request exists.
/// </para>
/// </remarks>
/// <param name="scopes">
/// Opens a scope to read <see cref="ICurrentUser"/>. Taking it as a constructor dependency
/// instead would be a singleton holding a scoped service — which is ZERO009, and which the
/// container rejected when this class was first written that way.
/// </param>
/// <param name="loggers">
/// Optional. A capability's entry point has to be sufficient on its own, and a bare
/// collection has no logging in it — requiring one here would mean
/// <c>AddZeroAuthorization()</c> alone no longer builds, which the contract test caught.
/// </param>
internal sealed class CurrentUserCheck(
    IServiceScopeFactory scopes, ILoggerFactory? loggers = null) : IHostedService
{
    private readonly ILogger _logger =
        (loggers ?? NullLoggerFactory.Instance).CreateLogger<CurrentUserCheck>();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();

        var user = scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        if (!ReferenceEquals(user, CurrentUser.Anonymous)) return Task.CompletedTask;

        _logger.LogWarning(
            "Authorization is registered but no ICurrentUser is. Every caller will be anonymous, " +
            "so every request that is not AllowAnonymous will be refused. Register one — in an " +
            "ASP.NET application: services.AddHttpContextAccessor() and services.AddScoped<ICurrentUser>(" +
            "sp => new ClaimsPrincipalCurrentUser(sp.GetRequiredService<IHttpContextAccessor>()" +
            ".HttpContext?.User ?? new ClaimsPrincipal())). If anonymous is what this application " +
            "means, register CurrentUser.Anonymous yourself and this line stops.");

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
