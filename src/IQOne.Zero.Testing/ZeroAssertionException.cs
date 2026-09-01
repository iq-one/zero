namespace IQOne.Zero.Testing;

/// <summary>
/// Thrown when an assertion from this package does not hold.
/// </summary>
/// <remarks>
/// <para>
/// A plain exception of our own rather than a test framework's failure type. The runner is
/// the consumer's choice, and throwing xunit's exception would make xunit a dependency of
/// every project that wanted to assert on a <see cref="Result"/>. Every runner reports an
/// unhandled exception as a failed test, and the message is where the explanation belongs.
/// </para>
/// <para>
/// The message always names what was actually there — the errors, the value, the requests
/// sent — because an assertion helper that only says "expected true" costs more time than
/// the hand-written check it replaced.
/// </para>
/// </remarks>
public sealed class ZeroAssertionException : Exception
{
    /// <summary>States what was expected and what was actually there.</summary>
    /// <param name="message">The explanation shown by the runner.</param>
    public ZeroAssertionException(string message) : base(message) { }

    /// <summary>States what was expected, what was there, and what went wrong underneath.</summary>
    /// <param name="message">The explanation shown by the runner.</param>
    /// <param name="innerException">The failure that led to this one.</param>
    public ZeroAssertionException(string message, Exception innerException) : base(message, innerException) { }
}
