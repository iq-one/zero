using IQOne.Zero.DependencyInjection.Descriptors;
using IQOne.Zero.Results;

namespace IQOne.Zero.Messaging;

/// <summary>Marker the generator uses to find handlers. Do not implement it directly.</summary>
public interface IRequestHandler : IScoped;

/// <summary>
/// Handles one request type.
/// </summary>
/// <remarks>
/// Exactly one handler per request. The pipeline, not the handler, carries validation,
/// authorization, logging, caching and transactions — a handler that does any of those is
/// doing a job that would then have to be repeated in the next handler.
/// </remarks>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface IRequestHandler<in TRequest, TResponse> : IRequestHandler
    where TRequest : IRequest<TResponse>
{
    /// <summary>Handles the request.</summary>
    /// <param name="request">What was asked for.</param>
    /// <param name="cancellationToken">Cancels the work.</param>
    /// <returns>The outcome. Expected failures are errors, not exceptions.</returns>
    Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

/// <summary>Handles a command that produces nothing.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Unit>
    where TCommand : ICommand;

/// <summary>Handles a command that produces a value.</summary>
/// <typeparam name="TCommand">The command handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

/// <summary>Handles a query.</summary>
/// <typeparam name="TQuery">The query handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public interface IQueryHandler<in TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
