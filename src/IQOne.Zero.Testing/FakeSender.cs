using System.Runtime.CompilerServices;
using IQOne.Zero.Messaging;

namespace IQOne.Zero.Testing;

/// <summary>
/// An <see cref="ISender"/> that records what was sent and returns a scripted outcome per
/// request type.
/// </summary>
/// <remarks>
/// <para>
/// For testing the code that sends — a service, a background job, a handler that delegates —
/// rather than the handler on the other end. The alternative is standing up a container with
/// every handler that code happens to reach, which turns a unit test into an integration test
/// and makes it fail for reasons that have nothing to do with the code under test.
/// </para>
/// <para>
/// Outcomes are keyed by the request's concrete type, exactly as the real sender dispatches.
/// A request nobody scripted throws rather than returning a default success: a silent default
/// would let the code under test pass while doing the wrong thing.
/// </para>
/// <para>
/// Recording is guarded by a lock, because code under test is free to send from several tasks
/// at once and a corrupted list would show up as a baffling test failure rather than as a
/// concurrency bug.
/// </para>
/// </remarks>
public sealed class FakeSender : ISender
{
    private readonly Dictionary<Type, Func<object, CancellationToken, object>> _script = [];
    private readonly List<object> _sent = [];
    private readonly object _gate = new();

    /// <summary>Scripts a successful outcome carrying this value.</summary>
    /// <typeparam name="TRequest">The request type to answer.</typeparam>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="value">What the caller receives.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Returns<TRequest, TResponse>(TResponse value)
        where TRequest : IRequest<TResponse>
        => Returns<TRequest, TResponse>(Result<TResponse>.Success(value));

    /// <summary>Scripts an outcome, successful or not.</summary>
    /// <typeparam name="TRequest">The request type to answer.</typeparam>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="result">What the caller receives.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Returns<TRequest, TResponse>(Result<TResponse> result)
        where TRequest : IRequest<TResponse>
        => Script<TRequest, TResponse>((_, _) => Task.FromResult(result));

    /// <summary>Scripts an outcome derived from the request.</summary>
    /// <typeparam name="TRequest">The request type to answer.</typeparam>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="respond">Produces the outcome from the request.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Returns<TRequest, TResponse>(Func<TRequest, Result<TResponse>> respond)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(respond);

        return Script<TRequest, TResponse>((request, _) => Task.FromResult(respond(request)));
    }

    /// <summary>Scripts an outcome produced asynchronously, so a test can observe the token.</summary>
    /// <typeparam name="TRequest">The request type to answer.</typeparam>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="respond">Produces the outcome from the request and the token.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Returns<TRequest, TResponse>(
        Func<TRequest, CancellationToken, Task<Result<TResponse>>> respond)
        where TRequest : IRequest<TResponse>
    {
        ArgumentNullException.ThrowIfNull(respond);

        return Script(respond);
    }

    /// <summary>Scripts a successful outcome for a command that produces nothing.</summary>
    /// <typeparam name="TRequest">The command type to answer.</typeparam>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Succeeds<TRequest>()
        where TRequest : IRequest<Unit>
        => Returns<TRequest, Unit>(Unit.Value);

    /// <summary>Scripts a failure.</summary>
    /// <typeparam name="TRequest">The request type to answer.</typeparam>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="error">Why it fails.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Fails<TRequest, TResponse>(Error error)
        where TRequest : IRequest<TResponse>
        => Returns<TRequest, TResponse>(Result<TResponse>.Failure(error));

    /// <summary>Scripts a failure for a command that produces nothing.</summary>
    /// <typeparam name="TRequest">The command type to answer.</typeparam>
    /// <param name="error">Why it fails.</param>
    /// <returns>This sender, for chaining.</returns>
    public FakeSender Fails<TRequest>(Error error)
        where TRequest : IRequest<Unit>
        => Returns<TRequest, Unit>(Result<Unit>.Failure(error));

    /// <summary>Everything sent through this sender, in order.</summary>
    public IReadOnlyList<object> Sent
    {
        get { lock (_gate) return [.. _sent]; }
    }

    /// <summary>Everything of one request type that was sent, in order.</summary>
    /// <typeparam name="TRequest">The request type of interest.</typeparam>
    /// <returns>The matching requests.</returns>
    public IReadOnlyList<TRequest> SentOf<TRequest>()
    {
        lock (_gate) return [.. _sent.OfType<TRequest>()];
    }

    /// <summary>Asserts that exactly one request of this type was sent.</summary>
    /// <typeparam name="TRequest">The request type expected.</typeparam>
    /// <returns>The request that was sent, so its contents can be checked.</returns>
    /// <exception cref="ZeroAssertionException">None, or more than one, was sent.</exception>
    public TRequest ShouldHaveSent<TRequest>()
    {
        var matches = SentOf<TRequest>();

        return matches.Count == 1
            ? matches[0]
            : throw new ZeroAssertionException(
                $"Expected exactly one {typeof(TRequest).Name} to be sent, but {matches.Count} " +
                $"{(matches.Count == 1 ? "was" : "were")}. {DescribeSent()}");
    }

    /// <summary>Asserts that a request of this type matching a condition was sent.</summary>
    /// <remarks>
    /// The condition's source text is captured by the compiler, so the failure names the
    /// expectation rather than reporting that a predicate returned false.
    /// </remarks>
    /// <typeparam name="TRequest">The request type expected.</typeparam>
    /// <param name="match">What the request must satisfy.</param>
    /// <param name="expression">Filled in by the compiler with the source text of <paramref name="match"/>.</param>
    /// <returns>The first matching request.</returns>
    /// <exception cref="ZeroAssertionException">Nothing sent matched.</exception>
    public TRequest ShouldHaveSent<TRequest>(
        Func<TRequest, bool> match,
        [CallerArgumentExpression(nameof(match))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(match);

        foreach (var request in SentOf<TRequest>())
            if (match(request))
                return request;

        throw new ZeroAssertionException(
            $"Expected a {typeof(TRequest).Name} satisfying {expression} to be sent, but none matched. " +
            DescribeSent());
    }

    /// <summary>Asserts how many requests of this type were sent.</summary>
    /// <typeparam name="TRequest">The request type expected.</typeparam>
    /// <param name="times">How many should have been sent.</param>
    /// <exception cref="ZeroAssertionException">A different number was sent.</exception>
    public void ShouldHaveSent<TRequest>(int times)
    {
        var count = SentOf<TRequest>().Count;

        if (count == times) return;

        throw new ZeroAssertionException(
            $"Expected {times} {typeof(TRequest).Name} to be sent, but {count} " +
            $"{(count == 1 ? "was" : "were")}. {DescribeSent()}");
    }

    /// <summary>Asserts that no request of this type was sent.</summary>
    /// <typeparam name="TRequest">The request type that should not appear.</typeparam>
    /// <exception cref="ZeroAssertionException">One was sent.</exception>
    public void ShouldNotHaveSent<TRequest>()
    {
        var count = SentOf<TRequest>().Count;

        if (count == 0) return;

        throw new ZeroAssertionException(
            $"Expected no {typeof(TRequest).Name} to be sent, but {count} " +
            $"{(count == 1 ? "was" : "were")}. {DescribeSent()}");
    }

    /// <summary>Asserts that nothing at all was sent.</summary>
    /// <exception cref="ZeroAssertionException">Something was sent.</exception>
    public void ShouldHaveSentNothing()
    {
        lock (_gate)
        {
            if (_sent.Count == 0) return;
        }

        throw new ZeroAssertionException($"Expected nothing to be sent. {DescribeSent()}");
    }

    /// <summary>Records the request and returns whatever was scripted for its type.</summary>
    /// <typeparam name="TResponse">What the request produces.</typeparam>
    /// <param name="request">What the code under test asked for.</param>
    /// <param name="cancellationToken">Passed to a scripted delegate that takes one.</param>
    /// <returns>The scripted outcome.</returns>
    /// <exception cref="InvalidOperationException">Nothing is scripted for this request type.</exception>
    public Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Func<object, CancellationToken, object>? respond;

        lock (_gate)
        {
            _sent.Add(request);
            _script.TryGetValue(request.GetType(), out respond);
        }

        if (respond is null)
            throw new InvalidOperationException(
                $"The fake sender has no outcome scripted for '{request.GetType().FullName}'. " +
                $"Call Returns<{request.GetType().Name}, TResponse>(...), " +
                $"Fails<{request.GetType().Name}, TResponse>(error) or, for a command, " +
                $"Succeeds<{request.GetType().Name}>() before the code under test runs.");

        return (Task<Result<TResponse>>)respond(request, cancellationToken);
    }

    private FakeSender Script<TRequest, TResponse>(
        Func<TRequest, CancellationToken, Task<Result<TResponse>>> respond)
        where TRequest : IRequest<TResponse>
    {
        // Boxed as object for the same reason the real dispatch table does it: the table is
        // untyped while the caller is not, and the cast on the way out is free.
        lock (_gate) _script[typeof(TRequest)] = (request, token) => respond((TRequest)request, token);

        return this;
    }

    private string DescribeSent()
    {
        List<object> snapshot;

        lock (_gate) snapshot = [.. _sent];

        return snapshot.Count == 0
            ? "Nothing was sent."
            : $"{snapshot.Count} {(snapshot.Count == 1 ? "request was" : "requests were")} sent:" +
              Environment.NewLine +
              string.Join(
                  Environment.NewLine,
                  snapshot.Select((request, index) => $"  [{index + 1}] {Explain.Value(request)}"));
    }
}
