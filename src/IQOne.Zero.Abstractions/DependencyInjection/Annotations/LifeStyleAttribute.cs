using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Annotations;

/// <summary>
/// Declares the lifetime of the annotated type or of every type implementing the
/// annotated interface.
/// </summary>
/// <remarks>
/// Applying this directly is rarely necessary: the marker interfaces in
/// <see cref="IQOne.Zero.DependencyInjection.Descriptors"/> already carry it. Use it when a
/// type's lifetime cannot be expressed by the abstraction it implements.
/// </remarks>
/// <param name="lifeStyle">The lifetime to register with.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public class LifeStyleAttribute(LifeStyle lifeStyle) : Attribute
{
    /// <summary>The declared lifetime.</summary>
    public LifeStyle LifeStyle { get; set; } = lifeStyle;

    /// <summary>The container lifetime <see cref="LifeStyle"/> maps to.</summary>
    public ServiceLifetime ServiceLifetime => ToServiceLifetime(LifeStyle);

    /// <summary>Maps a <see cref="LifeStyle"/> onto a container lifetime.</summary>
    /// <param name="lifeStyle">The lifetime to map.</param>
    /// <returns>The container lifetime to register with.</returns>
    public virtual ServiceLifetime ToServiceLifetime(LifeStyle lifeStyle) => lifeStyle switch
    {
        LifeStyle.Singleton => ServiceLifetime.Singleton,
        LifeStyle.Scoped => ServiceLifetime.Scoped,
        _ => ServiceLifetime.Transient
    };

    /// <inheritdoc />
    public override string ToString() => $"{LifeStyle}";
}

/// <summary>Registers the annotated type as a singleton.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class SingletonAttribute() : LifeStyleAttribute(LifeStyle.Singleton);

/// <summary>Registers the annotated type as scoped.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class ScopedAttribute() : LifeStyleAttribute(LifeStyle.Scoped);

/// <summary>Registers the annotated type as transient.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Interface)]
public sealed class TransientAttribute() : LifeStyleAttribute(LifeStyle.Transient);





