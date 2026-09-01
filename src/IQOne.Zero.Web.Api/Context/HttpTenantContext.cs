using IQOne.Zero.Data.Context;
using Microsoft.AspNetCore.Http;

namespace IQOne.Zero.Web.Api.Context;

/// <summary>Reads the tenant from the bearer token.</summary>
public sealed class HttpTenantContext(IHttpContextAccessor accessor, TenantOptions options) : ITenantContext
{
    private bool _suppressed;

    /// <summary>The claim carrying the tenant identifier.</summary>
    private const string TenantClaim = "aud";

    public int TenantId => Read(TenantClaim) ?? options.DefaultTenantId;

    public int MasterTenantId => options.MasterTenantId;

    public bool IsSuppressed => _suppressed;

    /// <inheritdoc />
    public IDisposable Suppress()
    {
        _suppressed = true;
        return new Scope(this);
    }

    private int? Read(string claimType)
        => int.TryParse(accessor.HttpContext?.User.FindFirst(claimType)?.Value, out var value) ? value : null;

    private sealed class Scope(HttpTenantContext owner) : IDisposable
    {
        public void Dispose() => owner._suppressed = false;
    }
}

public sealed class TenantOptions
{
    /// <summary>Tenant used when the request is unauthenticated.</summary>
    public int DefaultTenantId { get; set; }

    public int MasterTenantId { get; set; }
}

/// <summary>Reads the acting user from the bearer token.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public int LoginId => Read("CredentialId");

    public int IdentityId => Read("IdentityId");

    private int Read(string claimType)
        => int.TryParse(accessor.HttpContext?.User.FindFirst(claimType)?.Value, out var value) ? value : 0;
}