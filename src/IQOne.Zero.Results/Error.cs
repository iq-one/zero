namespace IQOne.Zero.Results;

/// <summary>What kind of failure occurred, independent of how any transport reports it.</summary>
/// <remarks>
/// Deliberately not an HTTP status code. The mapping from a kind to a status, an exit code
/// or a message on a queue belongs to the edge of the application, and an enum that already
/// says "404" has made that decision for every caller.
/// </remarks>
public enum ErrorKind
{
    /// <summary>The operation failed for a reason the caller cannot classify further.</summary>
    Failure,

    /// <summary>The input was not acceptable.</summary>
    Validation,

    /// <summary>What was asked for does not exist.</summary>
    NotFound,

    /// <summary>The current state does not allow the operation.</summary>
    Conflict,

    /// <summary>The caller could not be identified.</summary>
    Unauthorized,

    /// <summary>The caller is known but not permitted.</summary>
    Forbidden,

    /// <summary>A dependency was unavailable or timed out. Usually worth retrying.</summary>
    Unavailable
}

/// <summary>
/// One reason an operation did not succeed.
/// </summary>
/// <remarks>
/// <para>
/// An error is a value, so it can be returned, collected, compared and tested without a
/// stack unwind. Exceptions stay for the failures nobody planned for — a bug, a disk that
/// vanished — where a stack trace is the point.
/// </para>
/// <para>
/// <see cref="Code"/> is the stable identifier callers may branch on and translators may key
/// off. <see cref="Message"/> is for a human and may change without notice.
/// </para>
/// </remarks>
/// <param name="Code">Stable identifier, conventionally <c>area.reason</c>.</param>
/// <param name="Message">What went wrong, in terms the reader can act on.</param>
/// <param name="Kind">How the failure should be classified.</param>
public readonly record struct Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    /// <summary>The absence of an error. Carried by every successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Whether this is the absence of an error.</summary>
    public bool IsNone => string.IsNullOrEmpty(Code) && string.IsNullOrEmpty(Message);

    /// <summary>Optional data about the failure, for the caller that knows what to do with it.</summary>
    /// <remarks>Never put a message for a human here; that is <see cref="Message"/>.</remarks>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>A failure the caller cannot classify further.</summary>
    /// <param name="code">Stable identifier, conventionally <c>area.reason</c>.</param>
    /// <param name="message">What went wrong.</param>
    /// <returns>The error.</returns>
    public static Error Failure(string code, string message) => new(code, message);

    /// <summary>The input was not acceptable.</summary>
    /// <param name="code">Stable identifier, conventionally <c>area.reason</c>.</param>
    /// <param name="message">What is wrong and what would be accepted.</param>
    /// <returns>The error.</returns>
    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    /// <summary>What was asked for does not exist.</summary>
    /// <param name="code">Stable identifier.</param>
    /// <param name="message">What was not found.</param>
    /// <returns>The error.</returns>
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    /// <summary>The current state does not allow the operation.</summary>
    /// <param name="code">Stable identifier.</param>
    /// <param name="message">Which state, and what would allow it.</param>
    /// <returns>The error.</returns>
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    /// <summary>The caller could not be identified.</summary>
    /// <param name="code">Stable identifier.</param>
    /// <param name="message">What is missing.</param>
    /// <returns>The error.</returns>
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    /// <summary>The caller is known but not permitted.</summary>
    /// <param name="code">Stable identifier.</param>
    /// <param name="message">What permission is required.</param>
    /// <returns>The error.</returns>
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    /// <summary>A dependency was unavailable or timed out.</summary>
    /// <param name="code">Stable identifier.</param>
    /// <param name="message">Which dependency, and whether retrying is sensible.</param>
    /// <returns>The error.</returns>
    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);

    /// <summary>Returns this error with the given metadata attached.</summary>
    /// <param name="metadata">Data about the failure.</param>
    /// <returns>A copy carrying the metadata.</returns>
    public Error With(IReadOnlyDictionary<string, object?> metadata) => this with { Metadata = metadata };

    /// <inheritdoc />
    public override string ToString() => IsNone ? "(none)" : $"{Kind}: {Code} — {Message}";
}
