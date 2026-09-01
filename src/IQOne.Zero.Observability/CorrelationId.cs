using System.Diagnostics;

namespace IQOne.Zero.Observability;

/// <summary>
/// The one id that ties together everything done for a single request.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately not a context system. There is already an ambient identifier that
/// every tool understands and that crosses process boundaries on its own — the trace id of
/// the current <see cref="Activity"/>, propagated as a W3C <c>traceparent</c> header — and a
/// second one competing with it would only mean two ids in every log line and neither in
/// every tool.
/// </para>
/// <para>
/// So this type does two small things: it reads that id, and it lets a transport carry an id
/// that came from somewhere else — an <c>X-Correlation-Id</c> header, a message property, a
/// batch number — for callers who have to answer to a system that issued its own.
/// </para>
/// </remarks>
public static class CorrelationId
{
    private static readonly AsyncLocal<string?> Assigned = new();

    /// <summary>
    /// The id for the work in flight, or <see langword="null"/> when nothing has started one.
    /// </summary>
    /// <remarks>
    /// An id supplied by <see cref="Begin"/> wins, because it came from a caller who is
    /// tracking this work under a name of their own. Otherwise this is the current trace id,
    /// which a web host or a queue consumer will already have established.
    /// </remarks>
    public static string? Current => Assigned.Value ?? Activity.Current?.TraceId.ToString();

    /// <summary>
    /// Carries an id that came from outside for the rest of this asynchronous flow.
    /// </summary>
    /// <remarks>
    /// Call it at the edge — where the header is read — and dispose it there. The value flows
    /// into everything awaited inside the <c>using</c> and nowhere else, so two requests
    /// handled at once never see each other's id.
    /// </remarks>
    /// <param name="id">The id the caller is tracking this work under.</param>
    /// <returns>Restores the previous id when disposed.</returns>
    /// <exception cref="ArgumentException"><paramref name="id"/> is null, empty or whitespace.</exception>
    public static IDisposable Begin(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A correlation id must have a value.", nameof(id));

        var previous = Assigned.Value;
        Assigned.Value = id;

        return new Scope(previous);
    }

    /// <summary>The id supplied by <see cref="Begin"/>, ignoring the trace-id fallback.</summary>
    /// <remarks>
    /// Tracing tags an activity with the correlation id only when it is this one. Tagging a
    /// span with its own trace id would be a column of duplicated data in every trace.
    /// </remarks>
    internal static string? Supplied => Assigned.Value;

    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            Assigned.Value = previous;
        }
    }
}
