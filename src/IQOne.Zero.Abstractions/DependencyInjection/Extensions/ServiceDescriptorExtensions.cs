using System.Runtime.CompilerServices;
using IQOne.Zero.DependencyInjection.Descriptors;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.DependencyInjection.Extensions;

/// <summary>Reads what a <see cref="ServiceDescriptor"/> will produce, before it produces it.</summary>
public static class ServiceDescriptorExtensions
{
    /// <summary>
    /// One produced instance per descriptor, for the life of that descriptor.
    /// </summary>
    /// <remarks>
    /// The startup phases read the same descriptor more than once — configure-services,
    /// initialize, pre-run and shutdown each ask for the steps again. Producing a fresh
    /// object each time would mean a step that recorded something in one phase read a blank
    /// object in the next. Keyed weakly so a descriptor that goes away takes its instance
    /// with it.
    /// </remarks>
    private static readonly ConditionalWeakTable<ServiceDescriptor, object> Produced = new();

    /// <summary>
    /// The concrete type the descriptor will construct, whether it was registered by type,
    /// by instance or by factory.
    /// </summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns>The implementation type, or <see langword="null"/> when it cannot be determined.</returns>
    public static Type? GetImplementationType(this ServiceDescriptor? descriptor)
    {
        if (descriptor is null) return null;

        return descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
              ?? descriptor.KeyedImplementationInstance?.GetType()
              ?? descriptor.KeyedImplementationFactory?.GetType().GenericTypeArguments.ElementAtOrDefault(2)
            : descriptor.ImplementationType
              ?? descriptor.ImplementationInstance?.GetType()
              ?? descriptor.ImplementationFactory?.GetType().GenericTypeArguments.ElementAtOrDefault(1);
    }

    /// <summary>The instance the descriptor was registered with, if it holds one.</summary>
    /// <param name="descriptor">The descriptor to inspect.</param>
    /// <returns>The registered instance, or <see langword="null"/>.</returns>
    public static object? GetImplementationInstance(this ServiceDescriptor? descriptor)
        => descriptor is null ? null
            : descriptor.IsKeyedService ? descriptor.KeyedImplementationInstance
            : descriptor.ImplementationInstance;

    /// <summary>
    /// Produces the descriptor's instance without a service provider.
    /// </summary>
    /// <remarks>
    /// Only two registrations can be realised this way: one that already holds an instance,
    /// and one whose implementation is an <see cref="ISingletonInstance"/> — which is the
    /// promise that it can be built before the provider exists. Everything else needs the
    /// provider and returns <see langword="null"/> here.
    /// <para>
    /// A registration made by type is constructed rather than skipped. Skipping it is what
    /// made a generated startup step compile, register, pass validation and never run.
    /// </para>
    /// </remarks>
    /// <param name="descriptor">The descriptor to realise.</param>
    /// <returns>The instance, or <see langword="null"/> when it cannot be produced yet.</returns>
    /// <exception cref="InvalidOperationException">
    /// The implementation is an <see cref="ISingletonInstance"/> that cannot be constructed
    /// without the provider.
    /// </exception>
    public static object? ProduceImplementationInstance(this ServiceDescriptor? descriptor)
    {
        if (descriptor is null) return null;

        if (descriptor.GetImplementationInstance() is { } instance) return instance;

        if (!typeof(ISingletonInstance).IsAssignableFrom(descriptor.GetImplementationType())) return null;

        if (Produced.TryGetValue(descriptor, out var cached)) return cached;

        var created = Construct(descriptor);

        Produced.AddOrUpdate(descriptor, created);

        return created;
    }

    private static object Construct(ServiceDescriptor descriptor)
    {
        var implementationType = descriptor.GetImplementationType()!;

        if (Factory(descriptor) is { } factory)
            return factory()
                   ?? throw new InvalidOperationException(
                       $"The factory registered for '{Describe(descriptor)}' returned null while the " +
                       "service provider was being assembled.");

        if (implementationType.IsAbstract ||
            implementationType.ContainsGenericParameters ||
            implementationType.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException(
                $"'{implementationType.FullName}' implements {nameof(ISingletonInstance)}, which promises it " +
                "can be built before the service provider exists, but it cannot be constructed without one. " +
                "Give it a public parameterless constructor, or register it with an instance " +
                $"(services.AddSingleton<{Describe(descriptor)}>(new ...)).");

        return Activator.CreateInstance(implementationType)!;
    }

    /// <summary>
    /// The descriptor's factory, closed over the empty provider.
    /// </summary>
    /// <remarks>
    /// The provider does not exist yet, so the factory is handed one that answers null rather
    /// than a null reference. A factory that reaches for a service then fails with a message
    /// naming that service instead of a <see cref="NullReferenceException"/> from inside the
    /// framework.
    /// </remarks>
    private static Func<object?>? Factory(ServiceDescriptor descriptor)
        => descriptor.IsKeyedService
            ? descriptor.KeyedImplementationFactory is { } keyed
                ? () => keyed(NoServices.Instance, descriptor.ServiceKey)
                : null
            : descriptor.ImplementationFactory is { } factory
                ? () => factory(NoServices.Instance)
                : null;

    private static string Describe(ServiceDescriptor descriptor)
        => descriptor.ServiceType.Name;

    /// <summary>Stands in for the service provider that does not exist yet.</summary>
    private sealed class NoServices : IServiceProvider
    {
        public static readonly NoServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
