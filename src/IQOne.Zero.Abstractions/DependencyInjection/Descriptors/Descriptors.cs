using IQOne.Zero.DependencyInjection.Annotations;

namespace IQOne.Zero.DependencyInjection.Descriptors;

/// <summary>
/// Base of the lifetime marker interfaces.
/// </summary>
/// <remarks>
/// Lifetime is carried by the abstraction rather than declared at the registration site:
/// a type implementing <see cref="IScoped"/> is registered as scoped with no attribute and
/// no call. Registration happens at compile time, so the container performs no assembly
/// scanning at startup.
/// </remarks>
public interface IServiceDescriptor;

/// <summary>One instance for the lifetime of the application.</summary>
[Singleton] public interface ISingleton : IServiceDescriptor;

/// <summary>One instance per scope, which for a web application is one request.</summary>
[Scoped] public interface IScoped : IServiceDescriptor;

/// <summary>A new instance on every resolution.</summary>
[Transient] public interface ITransient : IServiceDescriptor;

/// <summary>One instance per thread.</summary>
[Thread] public interface IThread : IServiceDescriptor;

/// <summary>
/// A singleton whose instance is available before the service provider is built.
/// </summary>
/// <remarks>
/// Startup steps are discovered from <see cref="Microsoft.Extensions.DependencyInjection.IServiceCollection"/>
/// while the provider does not yet exist. Only a registration that already holds an
/// instance, or a type marked with this interface, can be produced at that point.
/// </remarks>
public interface ISingletonInstance : ISingleton;
