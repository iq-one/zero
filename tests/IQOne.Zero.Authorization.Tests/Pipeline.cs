using IQOne.Zero.Authorization;
using IQOne.Zero.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace IQOne.Zero.Authorization.Tests;

/// <summary>Records that a handler ran, which is the only thing these tests ask of one.</summary>
internal sealed class HandlerLog
{
    public bool Ran { get; set; }
}

/// <summary>A handler for any request producing a string. It exists to prove it was not reached.</summary>
internal sealed class Echo<TRequest>(HandlerLog log) : IRequestHandler<TRequest, string>
    where TRequest : IRequest<string>
{
    public Task<Result<string>> HandleAsync(TRequest request, CancellationToken cancellationToken)
    {
        log.Ran = true;

        return Task.FromResult(Result<string>.Success("handled"));
    }
}

/// <summary>
/// Builds a real pipeline around one request type.
/// </summary>
/// <remarks>
/// Authorization is only worth having if it cannot be skipped, so nothing here calls the
/// behaviour directly: every test sends a request the way the application does, and asks
/// whether the handler ran.
/// </remarks>
internal sealed class Pipeline
{
    private Pipeline(ISender sender, HandlerLog log)
    {
        Sender = sender;
        Log = log;
    }

    public ISender Sender { get; }

    public HandlerLog Log { get; }

    /// <summary>Whether the handler behind the request ran.</summary>
    public bool Reached => Log.Ran;

    public static Pipeline For<TRequest>(
        ICurrentUser? user = null,
        Action<AuthorizationOptions>? configure = null,
        Action<IServiceCollection>? register = null)
        where TRequest : IRequest<string>
    {
        var log = new HandlerLog();
        var services = new ServiceCollection();

        services.AddSingleton(log);
        services.AddScoped<IRequestHandler<TRequest, string>, Echo<TRequest>>();

        if (user is not null) services.AddScoped(_ => user);

        services.AddZeroAuthorization(configure);
        register?.Invoke(services);

        services.AddZeroMessaging(requests => requests.Add(new RequestEntry(
            typeof(TRequest), typeof(string), typeof(Echo<TRequest>),
            static (sp, r, ct) => RequestPipeline.RunAsync<TRequest, string>((TRequest)r, sp, ct))));

        return new Pipeline(services.BuildServiceProvider().GetRequiredService<ISender>(), log);
    }

    public Task<Result<string>> SendAsync(IRequest<string> request, CancellationToken cancellationToken = default)
        => Sender.SendAsync(request, cancellationToken);
}
