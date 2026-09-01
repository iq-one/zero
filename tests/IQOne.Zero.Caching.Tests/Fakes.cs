using IQOne.Zero.Caching;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IQOne.Zero.Caching.Tests;

/// <summary>Builds the store AddZeroCaching would have registered, by hand.</summary>
internal static class Store
{
    public static InMemoryCache InMemory() => new(new MemoryCache(Options.Create(new MemoryCacheOptions())));

    public static RecordingCache Recording() => new(InMemory());
}

internal sealed record GetInvoice(int Id) : IQuery<string>, ICacheable
{
    public string CacheKey => $"invoice:{Id}";
}

internal sealed record GetInvoiceLines(int Id) : IQuery<string>, ICacheable
{
    public string CacheKey => $"invoice:{Id}:lines";

    public TimeSpan? Lifetime => TimeSpan.FromMinutes(30);
}

internal sealed record GetSummary(int Id) : IQuery<string>;

/// <summary>The mistake ZERO210 reports, in the form the pipeline has to survive.</summary>
internal sealed record CloseInvoice(int Id) : ICommand<string>, ICacheable
{
    public string CacheKey => $"invoice:{Id}";
}

internal sealed record GetNothing(int Id) : IQuery<string?>, ICacheable
{
    public string CacheKey => $"nothing:{Id}";
}

internal sealed record GetWithoutKey(int Id) : IQuery<string>, ICacheable
{
    public string CacheKey => "   ";
}

internal sealed record GetExpiredOnArrival(int Id) : IQuery<string>, ICacheable
{
    public string CacheKey => $"expired:{Id}";

    public TimeSpan? Lifetime => TimeSpan.Zero;
}

/// <summary>
/// Answers differently every time it runs, so a cache hit is visible in the answer rather
/// than only in a call count.
/// </summary>
/// <typeparam name="TRequest">The request handled.</typeparam>
/// <typeparam name="TResponse">What handling it produces.</typeparam>
internal sealed class CountingHandler<TRequest, TResponse> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly Func<int, TResponse> _answer;

    public CountingHandler(Func<int, TResponse> answer) => _answer = answer;

    public int Calls { get; private set; }

    public Error? Refuse { get; set; }

    public CancellationToken Token { get; private set; }

    public Task<Result<TResponse>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        Token = cancellationToken;

        return Task.FromResult(Refuse is { } error
            ? Result<TResponse>.Failure(error)
            : Result<TResponse>.Success(_answer(Calls)));
    }
}

/// <summary>Wraps a store and records what the behaviour asked it to do.</summary>
/// <param name="inner">The store the calls are forwarded to.</param>
internal sealed class RecordingCache(ICache inner) : ICache
{
    public List<string> Reads { get; } = [];

    public List<(string Key, TimeSpan Lifetime)> Writes { get; } = [];

    public List<string> Removals { get; } = [];

    public List<CancellationToken> Tokens { get; } = [];

    public ValueTask<Cached<TValue>> GetAsync<TValue>(string key, CancellationToken cancellationToken)
    {
        Reads.Add(key);
        Tokens.Add(cancellationToken);

        return inner.GetAsync<TValue>(key, cancellationToken);
    }

    public ValueTask SetAsync<TValue>(
        string key, TValue value, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        Writes.Add((key, lifetime));
        Tokens.Add(cancellationToken);

        return inner.SetAsync(key, value, lifetime, cancellationToken);
    }

    public ValueTask RemoveAsync(string key, CancellationToken cancellationToken)
    {
        Removals.Add(key);
        Tokens.Add(cancellationToken);

        return inner.RemoveAsync(key, cancellationToken);
    }

    public ValueTask RemoveByPrefixAsync(string keyPrefix, CancellationToken cancellationToken)
    {
        Removals.Add(keyPrefix);
        Tokens.Add(cancellationToken);

        return inner.RemoveByPrefixAsync(keyPrefix, cancellationToken);
    }
}

/// <summary>
/// Assembles the real pipeline around a handler.
/// </summary>
/// <remarks>
/// Caching is only worth having if it cannot be bypassed, so every test here sends through
/// <see cref="ISender"/> rather than calling the behaviour directly.
/// </remarks>
internal sealed class TestApplication
{
    private readonly ServiceCollection _services = [];
    private readonly List<RequestEntry> _entries = [];

    private TestApplication(ICache? cache, Action<CachingOptions>? configure)
    {
        // Before AddZeroCaching, which is where a consumer swapping the store puts it too.
        if (cache is not null) _services.AddSingleton(cache);

        _services.AddZeroCaching(configure);
    }

    public static TestApplication With(Action<CachingOptions>? configure = null)
        => new(null, configure);

    public static TestApplication With(ICache cache, Action<CachingOptions>? configure = null)
        => new(cache, configure);

    public CountingHandler<TRequest, TResponse> Handles<TRequest, TResponse>(Func<int, TResponse> answer)
        where TRequest : IRequest<TResponse>
    {
        var handler = new CountingHandler<TRequest, TResponse>(answer);

        _services.AddScoped<IRequestHandler<TRequest, TResponse>>(_ => handler);

        _entries.Add(new RequestEntry(
            typeof(TRequest), typeof(TResponse), handler.GetType(),
            static (sp, request, ct) => RequestPipeline.RunAsync<TRequest, TResponse>((TRequest)request, sp, ct)));

        return handler;
    }

    public CountingHandler<TRequest, string> Handles<TRequest>()
        where TRequest : IRequest<string>
        => Handles<TRequest, string>(calls => $"answer {calls}");

    public RunningApplication Build()
    {
        _services.AddZeroMessaging(requests =>
        {
            foreach (var entry in _entries) requests.Add(entry);
        });

        return new RunningApplication(_services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true
        }));
    }
}

/// <summary>
/// Sends each request from its own scope, the way a host would.
/// </summary>
/// <remarks>
/// A second send has to survive a new scope to prove anything: an answer that only outlives
/// the scope it was produced in has not been cached, it has merely not been thrown away yet.
/// </remarks>
/// <param name="provider">The built container.</param>
internal sealed class RunningApplication(ServiceProvider provider) : IDisposable
{
    public ICacheInvalidator Invalidator => provider.GetRequiredService<ICacheInvalidator>();

    public async Task<Result<TResponse>> SendAsync<TResponse>(
        IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().SendAsync(request, cancellationToken);
    }

    public void Dispose() => provider.Dispose();
}
