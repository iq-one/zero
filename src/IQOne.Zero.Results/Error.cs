using System.Collections.ObjectModel;

namespace IQOne.Zero;

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
/// <para>
/// Two errors are equal when their code, message, kind and metadata <em>contents</em> match.
/// Metadata is compared entry by entry rather than by reference, because an error is a value:
/// the same failure produced twice has to compare equal, or <c>Assert.Equal</c> and a
/// <c>HashSet</c> both quietly stop working.
/// </para>
/// </remarks>
/// <param name="Code">Stable identifier, conventionally <c>area.reason</c>.</param>
/// <param name="Message">What went wrong, in terms the reader can act on.</param>
/// <param name="Kind">How the failure should be classified.</param>
public readonly record struct Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    private readonly IReadOnlyDictionary<string, object?>? _metadata;

    /// <summary>The absence of an error. Carried by every successful result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>
    /// The reason carried by a failure that has none of its own.
    /// </summary>
    /// <remarks>
    /// A <see langword="default"/> result is a failure, and a failure with an empty reason
    /// list breaks everything downstream that reads the first error — a status-code mapping,
    /// a log line, a problem response. Substituting this keeps "a failure always states at
    /// least one reason" true however the result came about.
    /// </remarks>
    public static readonly Error Uninitialised = new(
        "result.uninitialised",
        "This result was never initialised, so it carries no reason of its own. A default result is a failure.");

    /// <summary>Whether this is the absence of an error.</summary>
    /// <remarks>
    /// This, not <see cref="Kind"/>, is how to tell a success apart: <see cref="None"/> has
    /// to have some kind, and the one it has is <see cref="ErrorKind.Failure"/>.
    /// </remarks>
    public bool IsNone => string.IsNullOrEmpty(Code) && string.IsNullOrEmpty(Message);

    /// <summary>Optional data about the failure, for the caller that knows what to do with it.</summary>
    /// <remarks>
    /// Never put a message for a human here; that is <see cref="Message"/>. What is stored is
    /// a copy of what was supplied, so an error cannot change after it has been returned; keys
    /// compare ordinally, and an empty dictionary is stored as <see langword="null"/>, since
    /// both mean "no metadata".
    /// </remarks>
    public IReadOnlyDictionary<string, object?>? Metadata
    {
        get => _metadata;
        init => _metadata = Copy(value);
    }

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
    /// <param name="metadata">Data about the failure. Copied, so later changes to it are not seen.</param>
    /// <returns>A copy carrying the metadata.</returns>
    public Error With(IReadOnlyDictionary<string, object?> metadata) => this with { Metadata = metadata };

    /// <inheritdoc />
    public override string ToString() => IsNone ? "(none)" : $"{Kind}: {Code} — {Message}";

    /// <summary>Whether two errors describe the same failure, metadata contents included.</summary>
    /// <param name="other">The error to compare with.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    /// <remarks>
    /// Written by hand because the generated one compares <see cref="Metadata"/> by
    /// reference, which makes two identically built errors unequal.
    /// </remarks>
    public bool Equals(Error other)
        => Kind == other.Kind
        && string.Equals(Code, other.Code, StringComparison.Ordinal)
        && string.Equals(Message, other.Message, StringComparison.Ordinal)
        && SameMetadata(_metadata, other._metadata);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();

        hash.Add(Code);
        hash.Add(Message);
        hash.Add((int)Kind);
        hash.Add(MetadataHash(_metadata));

        return hash.ToHashCode();
    }

    /// <summary>
    /// A snapshot of what the caller supplied.
    /// </summary>
    /// <remarks>
    /// Without this an error is only as immutable as the dictionary someone else still holds
    /// a reference to, and a value type that reports different data over time is not a value.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?>? Copy(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null || metadata.Count == 0) return null;

        var copy = new Dictionary<string, object?>(metadata.Count, StringComparer.Ordinal);

        foreach (var entry in metadata) copy[entry.Key] = entry.Value;

        return new ReadOnlyDictionary<string, object?>(copy);
    }

    private static bool SameMetadata(
        IReadOnlyDictionary<string, object?>? left, IReadOnlyDictionary<string, object?>? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;
        if (left.Count != right.Count) return false;

        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var value)) return false;
            if (!Equals(entry.Value, value)) return false;
        }

        return true;
    }

    /// <summary>A hash that does not depend on the order the entries happen to enumerate in.</summary>
    private static int MetadataHash(IReadOnlyDictionary<string, object?>? metadata)
    {
        if (metadata is null) return 0;

        var hash = metadata.Count;

        foreach (var entry in metadata) hash ^= HashCode.Combine(entry.Key, entry.Value);

        return hash;
    }
}
