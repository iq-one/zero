namespace IQOne.Zero.DependencyInjection.Annotations;

/// <summary>
/// A service's lifetime, mapped onto
/// <see cref="Microsoft.Extensions.DependencyInjection.ServiceLifetime"/> at registration.
/// </summary>
/// <remarks>
/// One value per lifetime the container actually has. An earlier version offered Thread,
/// Pooled, Bound and Custom as well — none of which the container can express, so all four
/// registered as transient. A name that promises something the runtime cannot do is worse
/// than no name: it is chosen deliberately and then does something else.
/// </remarks>
public enum LifeStyle
{
    /// <summary>One instance for the lifetime of the application.</summary>
    Singleton = 1,

    /// <summary>One instance per scope, which for a web application is one request.</summary>
    Scoped = 2,

    /// <summary>A new instance on every resolution.</summary>
    Transient = 3
}
