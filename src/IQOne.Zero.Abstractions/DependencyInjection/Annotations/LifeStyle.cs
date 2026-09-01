namespace IQOne.Zero.DependencyInjection.Annotations;

/// <summary>
/// Zero's own lifetime vocabulary, mapped onto
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime"/> at registration.
/// </summary>
/// <remarks>
/// The enum is wider than the container's three lifetimes so that a host can express
/// concepts the container has no name for. Values without a container equivalent map to
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient"/> unless
/// <see cref="LifeStyleAttribute.ToServiceLifetime"/> is overridden.
/// </remarks>
public enum LifeStyle
{
    /// <summary>No lifetime declared.</summary>
    Undefined = 0,

    /// <summary>One instance for the lifetime of the application.</summary>
    Singleton = 1,

    /// <summary>One instance per thread.</summary>
    Thread = 2,

    /// <summary>A new instance on every resolution.</summary>
    Transient = 3,

    /// <summary>Instances are taken from and returned to a pool.</summary>
    Pooled = 4,

    /// <summary>Lifetime is decided by a host-supplied rule.</summary>
    Custom = 6,

    /// <summary>One instance per scope, which for a web application is one request.</summary>
    Scoped = 7,

    /// <summary>Lifetime is bound to another object's lifetime.</summary>
    Bound = 8
}
