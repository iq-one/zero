using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Accessors;

/// <summary>Base of the accessor interfaces, which expose one value to their holder.</summary>
public interface IAccessor;

/// <summary>Exposes a single value of type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The exposed value's type.</typeparam>
public interface IObjectAccessor<out T> : IAccessor
{
    /// <summary>The exposed value.</summary>
    T Value { get; }
}

/// <summary>Exposes the service collection an object is building against.</summary>
public interface IServiceCollectionAccessor : IAccessor
{
    /// <summary>Registrations gathered so far. Valid before the provider is built.</summary>
    IServiceCollection ServiceCollection { get; set; }
}

/// <summary>Exposes the service provider an object resolves from.</summary>
public interface IServiceProviderAccessor : IAccessor
{
    /// <summary>The built provider. Valid only after the configure-services phase.</summary>
    IServiceProvider ServiceProvider { get; set; }
}
