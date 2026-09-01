using IQOne.Zero.Messaging;

namespace IQOne.Zero.Testing;

/// <summary>
/// A handler that returns what the test told it to and remembers what it was asked.
/// </summary>
/// <remarks>
/// Most tests of a behaviour are really assertions about the handler: that it ran, that it
/// did not, that it saw the request the caller sent. Writing a one-off handler for each of
/// those ends in a static <c>Ran</c> flag that leaks between tests — this keeps the state on
/// the instance, where the test can see it and the next test cannot.
/// </remarks>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
public sealed class StubHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Func<TRequest, CancellationToken, Task<Result<TResponse>>> _respond;
    private readonly List<TRequest> _received = [];
    private readonly object _gate = new();

    /// <summary>A handler that always produces the same outcome.</summary>
    /// <param name="result">What every call returns.</param>
    public StubHandler(Result<TResponse> result) => _respond = (_, _) => Task.FromResult(result);

    /// <summary>A handler whose outcome is derived from the request.</summary>
    /// <param name="respond">Produces the outcome. Receives the request and the token.</param>
    public StubHandler(Func<TRequest, CancellationToken, Task<Result<TResponse>>> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        _respond = respond;
    }

    /// <summary>A handler that succeeds with this value.</summary>
    /// <param name="value">What handling produces.</param>
    /// <returns>The handler.</returns>
    public static StubHandler<TRequest, TResponse> Returning(TResponse value)
        => new(Result<TResponse>.Success(value));

    /// <summary>A handler that fails with this error.</summary>
    /// <param name="error">Why handling fails.</param>
    /// <returns>The handler.</returns>
    public static StubHandler<TRequest, TResponse> Failing(Error error)
        => new(Result<TResponse>.Failure(error));

    /// <summary>Whether the handler was reached at all.</summary>
    public bool Ran
    {
        get { lock (_gate) return _received.Count > 0; }
    }

    /// <summary>Every request the handler received, in the order it received them.</summary>
    public IReadOnlyList<TRequest> Received
    {
        get { lock (_gate) return [.. _received]; }
    }

    /// <summary>Asserts that the pipeline reached the handler.</summary>
    /// <returns>The last request it received.</returns>
    /// <exception cref="ZeroAssertionException">The handler never ran.</exception>
    public TRequest ShouldHaveRun()
    {
        lock (_gate)
        {
            return _received.Count > 0
                ? _received[^1]
                : throw new ZeroAssertionException(
                    $"Expected the handler for {typeof(TRequest).Name} to run, but nothing reached it. " +
                    "A behaviour placed before it returned a failure without calling next().");
        }
    }

    /// <summary>Asserts that the pipeline stopped before the handler.</summary>
    /// <remarks>
    /// The assertion that makes validation and authorization worth having: rejecting the
    /// request is only half of it, the other half is that no work was done.
    /// </remarks>
    /// <exception cref="ZeroAssertionException">The handler ran. The message shows what it received.</exception>
    public void ShouldNotHaveRun()
    {
        lock (_gate)
        {
            if (_received.Count == 0) return;

            throw new ZeroAssertionException(
                $"Expected nothing to reach the handler for {typeof(TRequest).Name}, but it received " +
                $"{_received.Count} {(_received.Count == 1 ? "request" : "requests")}:{Environment.NewLine}"
                + string.Join(
                    Environment.NewLine,
                    _received.Select((request, index) => $"  [{index + 1}] {Explain.Value(request)}")));
        }
    }

    /// <inheritdoc />
    public Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        lock (_gate) _received.Add(request);

        return _respond(request, cancellationToken);
    }
}

/// <summary>
/// A behaviour that writes a line when it is entered and another when it is left.
/// </summary>
/// <remarks>
/// Ordering is the part of the pipeline that goes wrong silently: a transaction that opens
/// outside logging, an authorization check that runs after the data was read. Two of these
/// sharing one log turn that into an assertion on a list of strings.
/// </remarks>
/// <typeparam name="TRequest">The request wrapped.</typeparam>
/// <typeparam name="TResponse">What handling produces.</typeparam>
/// <param name="log">Where the entries are written. Share one list between behaviours.</param>
/// <param name="name">Identifies this behaviour in the log.</param>
/// <param name="order">Position in the pipeline; lower runs further out.</param>
public sealed class RecordingBehavior<TRequest, TResponse>(IList<string> log, string name, int order = 0)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => order;

    /// <inheritdoc />
    public async Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);

        log.Add($"{name}:in");

        var result = await next().ConfigureAwait(false);

        log.Add($"{name}:out");

        return result;
    }
}

/// <summary>
/// A behaviour that fails the request without calling the rest of the pipeline.
/// </summary>
/// <remarks>
/// Stands in for whatever would reject the request in production — a validator, an
/// authorization check, a circuit breaker — when the test is about what the caller sees
/// rather than about why it was rejected.
/// </remarks>
/// <typeparam name="TRequest">The request wrapped.</typeparam>
/// <typeparam name="TResponse">What handling produces.</typeparam>
/// <param name="error">Why the request is refused.</param>
/// <param name="order">Position in the pipeline. Defaults to where validation sits.</param>
public sealed class ShortCircuitBehavior<TRequest, TResponse>(Error error, int order = BehaviorOrder.Validation)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    /// <inheritdoc />
    public int Order => order;

    /// <inheritdoc />
    public Task<Result<TResponse>> HandleAsync(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        => Task.FromResult(Result<TResponse>.Failure(error));
}
