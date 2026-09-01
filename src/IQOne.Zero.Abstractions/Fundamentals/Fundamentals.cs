using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Fundamentals;

/// <summary>
/// A unit of startup work. Implementations are registered as singletons.
/// </summary>
/// <remarks>
/// Each fundamental in this namespace carries its own lifetime. A type implementing
/// <see cref="IProvider"/> is transient without stating so; the abstraction has already
/// said it. This keeps the lifetime decision next to the role rather than scattered
/// across registration call sites.
/// </remarks>
public interface IStep : ISingleton;

/// <summary>Creates instances of another type. Registered as a singleton.</summary>
public interface IFactory : ISingleton;

/// <summary>Supplies a value on demand. Registered as transient.</summary>
public interface IProvider : ITransient;

/// <summary>Supplies a value of type <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TResult">The supplied value's type.</typeparam>
public interface IProvider<out TResult> : IProvider
{
    /// <summary>Supplies the value.</summary>
    TResult Provide();
}

/// <summary>Supplies a value derived from an argument.</summary>
/// <typeparam name="TArgument">The input the value is derived from.</typeparam>
/// <typeparam name="TResult">The supplied value's type.</typeparam>
public interface IProvider<in TArgument, out TResult> : IProvider
{
    /// <summary>Supplies the value for <paramref name="argument"/>.</summary>
    TResult Provide(TArgument argument);
}

/// <summary>Supplies a value asynchronously.</summary>
public interface IAsyncProvider : IProvider;

/// <summary>Supplies a value of type <typeparamref name="TResult"/> asynchronously.</summary>
/// <typeparam name="TResult">The supplied value's type.</typeparam>
public interface IAsyncProvider<TResult> : IAsyncProvider
{
    /// <summary>Supplies the value.</summary>
    Task<TResult> ProvideAsync(CancellationToken cancellationToken);
}

/// <summary>Supplies a value derived from an argument, asynchronously.</summary>
/// <typeparam name="TArgument">The input the value is derived from.</typeparam>
/// <typeparam name="TResult">The supplied value's type.</typeparam>
public interface IAsyncProvider<in TArgument, TResult> : IAsyncProvider
{
    /// <summary>Supplies the value for <paramref name="argument"/>.</summary>
    Task<TResult> ProvideAsync(TArgument argument, CancellationToken cancellationToken);
}

/// <summary>Assembles an object across several calls. Registered as transient.</summary>
public interface IBuilder : ITransient;

/// <summary>Assembles a <typeparamref name="T"/>.</summary>
/// <typeparam name="T">The assembled type.</typeparam>
public interface IBuilder<out T> : IBuilder
{
    /// <summary>Produces the assembled object.</summary>
    T Build();
}

/// <summary>Assembles an object asynchronously.</summary>
public interface IAsyncBuilder : IBuilder;

/// <summary>Assembles a <typeparamref name="T"/> asynchronously.</summary>
/// <typeparam name="T">The assembled type.</typeparam>
public interface IAsyncBuilder<T> : IAsyncBuilder
{
    /// <summary>Produces the assembled object.</summary>
    Task<T> BuildAsync(CancellationToken cancellationToken);
}

/// <summary>Converts one representation into another. Registered as transient.</summary>
public interface IAdapter : ITransient;

/// <summary>Converts a <typeparamref name="TSource"/> into a <typeparamref name="TResult"/>.</summary>
/// <typeparam name="TSource">The input representation.</typeparam>
/// <typeparam name="TResult">The output representation.</typeparam>
public interface IAdapter<in TSource, out TResult> : IAdapter
{
    /// <summary>Converts <paramref name="value"/>.</summary>
    TResult Adapt(TSource value);
}

/// <summary>Alters an existing instance in place.</summary>
public interface IDecorator;

/// <summary>Alters an instance of <typeparamref name="TInstance"/>.</summary>
/// <typeparam name="TInstance">The altered type.</typeparam>
public interface IDecorator<in TInstance> : IDecorator
{
    /// <summary>Alters <paramref name="instance"/>.</summary>
    void Decorate(TInstance instance);
}

/// <summary>Alters an instance using additional arguments.</summary>
/// <typeparam name="TInstance">The altered type.</typeparam>
/// <typeparam name="TArguments">Arguments that steer the alteration.</typeparam>
public interface IDecorator<in TInstance, in TArguments> : IDecorator
{
    /// <summary>Alters <paramref name="instance"/> using <paramref name="arguments"/>.</summary>
    void Decorate(TInstance instance, TArguments arguments);
}

/// <summary>Applies settings to another object. Registered as transient.</summary>
public interface IConfigurator : ITransient;

/// <summary>Carries ambient information for the current operation. Registered as transient.</summary>
public interface IContext : ITransient;

/// <summary>A settings object. Registered as transient.</summary>
public interface IOption : ITransient;
