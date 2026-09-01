using IQOne.Zero.Messaging.Dispatch;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Primitives;

namespace IQOne.Zero.Web.Api.Routing;

/// <summary>
/// Materializes one <see cref="RouteEndpoint"/> per generated service method.
/// </summary>
/// <remarks>
/// A single catch-all route would swallow unrelated paths and could not carry
/// per-endpoint authorization, rate limiting, caching or OpenAPI metadata.
/// </remarks>
public sealed class ServiceEndpointDataSource : EndpointDataSource
{
    private readonly List<Endpoint> _endpoints;

    public ServiceEndpointDataSource(ServiceRegistry registry, ServiceEndpointOptions options)
        => _endpoints = [.. registry.Entries.Select(entry => Build(entry, options))];

    public override IReadOnlyList<Endpoint> Endpoints => _endpoints;

    /// <summary>The table is frozen at startup, so no change notification is needed.</summary>
    public override IChangeToken GetChangeToken() => NullChangeToken.Singleton;

    private static Endpoint Build(ServiceEntry entry, ServiceEndpointOptions options)
    {
        var route = $"{options.Prefix}{entry.Module}/{entry.Service}/{entry.Method}";

        var builder = new RouteEndpointBuilder(
            requestDelegate: context => ServiceEndpointHandler.HandleAsync(context, entry),
            routePattern: RoutePatternFactory.Parse(route),
            order: 0)
        {
            DisplayName = $"{entry.Module}/{entry.Service}/{entry.Method}"
        };

        builder.Metadata.Add(new HttpMethodMetadata(options.HttpMethods));
        builder.Metadata.Add(entry);

        // Request and response types, so the endpoint appears fully in the OpenAPI document.
        // ApiExplorer only surfaces endpoints carrying MethodInfo metadata, which a raw
        // RequestDelegate does not produce on its own.
        var handleMethod = entry.HandlerType.GetMethod("HandleAsync");

        if (handleMethod is not null)
            builder.Metadata.Add(handleMethod);

        builder.Metadata.Add(new ServiceAcceptsMetadata(entry.RequestType));
        builder.Metadata.Add(new ServiceProducesMetadata(entry.ResponseType));
        builder.Metadata.Add(new TagsAttribute(entry.Module));

        foreach (var metadata in entry.Metadata)
            builder.Metadata.Add(metadata);

        return builder.Build();
    }

    private sealed class NullChangeToken : IChangeToken
    {
        public static readonly NullChangeToken Singleton = new();

        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;

        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state)
            => NullDisposable.Instance;

        private sealed class NullDisposable : IDisposable
        {
            public static readonly NullDisposable Instance = new();
            public void Dispose() { }
        }
    }
}

public sealed class ServiceEndpointOptions
{
    /// <summary>Optional route prefix, for example <c>api/v1/</c>.</summary>
    public string Prefix { get; set; } = string.Empty;

    public string[] HttpMethods { get; set; } = ["GET", "POST"];
}

/// <summary>Declares the request body type of a generated endpoint.</summary>
internal sealed class ServiceAcceptsMetadata(Type requestType) : IAcceptsMetadata
{
    public IReadOnlyList<string> ContentTypes { get; } = ["application/json"];

    public Type? RequestType { get; } = requestType;

    public bool IsOptional => false;
}

/// <summary>Declares the response type of a generated endpoint.</summary>
internal sealed class ServiceProducesMetadata(Type responseType) : IProducesResponseTypeMetadata
{
    public Type? Type { get; } =
        typeof(IQOne.Zero.Messaging.ServiceResponse<>).MakeGenericType(responseType);

    public int StatusCode => StatusCodes.Status200OK;

    public IEnumerable<string> ContentTypes { get; } = ["application/json"];
}
