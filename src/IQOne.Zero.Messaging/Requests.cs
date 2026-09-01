using IQOne.Zero;

namespace IQOne.Zero.Messaging;

/// <summary>
/// Something the application is asked to do, carrying everything needed to do it.
/// </summary>
/// <remarks>
/// A request is data, not behaviour. Keeping the two apart is what lets the pipeline wrap
/// every request the same way — validating, authorising, logging, caching — without any of
/// those concerns knowing what the request means.
/// </remarks>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface IRequest<TResponse>;

/// <summary>
/// A request that changes something.
/// </summary>
/// <remarks>
/// The distinction from <see cref="IQuery{TResponse}"/> is not enforced by the compiler, and
/// it is not decoration: the pipeline reads it. A command is not cached and may open a
/// transaction; a query is neither.
/// </remarks>
public interface ICommand : IRequest<Unit>;

/// <summary>A request that changes something and returns a value.</summary>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>
/// A request that reads without changing anything.
/// </summary>
/// <remarks>Safe to cache and safe to retry. If neither is true, it is a command.</remarks>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface IQuery<TResponse> : IRequest<TResponse>;

/// <summary>
/// The absence of a value, for a command that produces nothing.
/// </summary>
/// <remarks>
/// <c>Result&lt;Unit&gt;</c> rather than a separate non-generic result keeps one shape through
/// the whole pipeline, so a behaviour never needs two versions of itself.
/// </remarks>
public readonly record struct Unit
{
    /// <summary>The only value there is.</summary>
    public static readonly Unit Value = default;

    /// <summary>A successful outcome producing nothing.</summary>
    public static Result<Unit> Success => Result<Unit>.Success(Value);

    /// <inheritdoc />
    public override string ToString() => "()";
}
