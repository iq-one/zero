namespace IQOne.Zero.Messaging.Dispatch;

/// <summary>
/// One row of the generated dispatch table.
/// </summary>
/// <remarks>
/// <see cref="Invoke"/> takes an already-deserialized request and returns the handler's
/// payload. No serializer and no response envelope appear in this contract: reading the
/// request and writing the response are the transport's job, so an application can keep its
/// own wire format without the framework knowing it.
/// </remarks>
/// <param name="Module">First route segment.</param>
/// <param name="Service">Second route segment.</param>
/// <param name="Method">Third route segment.</param>
/// <param name="RequestType">Concrete request type the transport deserializes into.</param>
/// <param name="ResponseType">Payload type the handler returns.</param>
/// <param name="HandlerType">The handler that serves this entry.</param>
/// <param name="Invoke">Resolves the handler and runs it.</param>
public sealed record ServiceEntry(
    string Module,
    string Service,
    string Method,
    Type RequestType,
    Type ResponseType,
    Type HandlerType,
    Func<IServiceProvider, object, CancellationToken, Task<object?>> Invoke)
{
    /// <summary>Metadata copied onto the generated endpoint.</summary>
    public IReadOnlyList<object> Metadata { get; init; } = [];

    /// <summary>The entry's route, as <c>module/service/method</c>.</summary>
    public string Route => $"{Module}/{Service}/{Method}";
}
