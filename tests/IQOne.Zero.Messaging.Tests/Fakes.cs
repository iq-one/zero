using IQOne.Zero.Messaging;
using IQOne.Zero.Results;

namespace IQOne.Zero.Messaging.Tests;

internal sealed record Greet(string Name) : IQuery<string>;

internal sealed record Fail : ICommand;

internal sealed record Unhandled : ICommand;

internal sealed class GreetHandler : IQueryHandler<Greet, string>
{
    public Task<Result<string>> HandleAsync(Greet query, CancellationToken cancellationToken)
        => Task.FromResult(Result<string>.Success($"Hello, {query.Name}."));
}

internal sealed class FailHandler : ICommandHandler<Fail>
{
    public static readonly Error Refused = Error.Conflict("fail.refused", "Refused on purpose.");

    public Task<Result<Unit>> HandleAsync(Fail command, CancellationToken cancellationToken)
        => Task.FromResult(Result<Unit>.Failure(Refused));
}

/// <summary>Records the order behaviours ran in, so the ordering contract can be asserted.</summary>
internal sealed class RecordingBehavior<TRequest, TResponse>(List<string> log, string name, int order)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Order => order;

    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        log.Add($"{name}:in");

        var result = await next();

        log.Add($"{name}:out");

        return result;
    }
}

/// <summary>Stops the pipeline before the handler, the way validation does.</summary>
internal sealed class ShortCircuitBehavior<TRequest, TResponse>(Error error)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public int Order => BehaviorOrder.Validation;

    public Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Task.FromResult(Result<TResponse>.Failure(error));
}
