using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zero.Sample.Orders.Host;

/// <summary>
/// Reads the caller from headers, for the sample only.
/// </summary>
/// <remarks>
/// <para>
/// A real application uses a real scheme. This exists so the authorization in the sample is
/// something you can exercise rather than something you have to take on trust: send
/// <c>X-Customer</c> and <c>X-Permissions</c> and watch the endpoints and the resource
/// requirement decide.
/// </para>
/// <para>
/// Making every endpoint anonymous instead would have removed the ceremony and the
/// demonstration with it.
/// </para>
/// </remarks>
/// <param name="options">Scheme options, unused here.</param>
/// <param name="logger">Where the framework logs authentication.</param>
/// <param name="encoder">Encodes redirect URLs, unused here.</param>
public sealed class HeaderAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme's name.</summary>
    public const string SchemeName = "Header";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Customer", out var customer) || customer.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, customer[0]!) };

        if (Request.Headers.TryGetValue("X-Permissions", out var permissions))
            claims.AddRange(permissions
                .SelectMany(value => (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(permission => new Claim("permission", permission.Trim())));

        var identity = new ClaimsIdentity(claims, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
