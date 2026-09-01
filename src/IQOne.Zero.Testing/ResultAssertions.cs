using System.Runtime.CompilerServices;

namespace IQOne.Zero.Testing;

/// <summary>
/// Assertions on <see cref="Result"/> and <see cref="Result{TValue}"/>.
/// </summary>
/// <remarks>
/// <para>
/// These exist for their failure messages. <c>result.IsSuccess.Should().BeTrue()</c> reports
/// "expected True, found False" and leaves the reader to open a debugger to find out which
/// error was returned; <see cref="ShouldSucceed(Result)"/> prints every error, with its code,
/// kind and message.
/// </para>
/// <para>
/// Named <c>ShouldSucceed</c> rather than <c>Should().Succeed()</c> on purpose: FluentAssertions
/// already defines <c>Should()</c> on <see cref="object"/>, and a second one would make every
/// call in a project that uses both ambiguous. This package works alongside any runner and any
/// assertion library rather than competing with one.
/// </para>
/// <para>
/// Each assertion returns what a test usually wants next — the value, the error, the error
/// list — so the outcome can be inspected further without unpacking the result by hand.
/// </para>
/// </remarks>
public static class ResultAssertions
{
    /// <summary>Asserts that the operation succeeded.</summary>
    /// <param name="result">The outcome under test.</param>
    /// <returns>The same result, for chaining.</returns>
    /// <exception cref="ZeroAssertionException">It failed. The message lists every error.</exception>
    public static Result ShouldSucceed(this Result result)
        => result.IsSuccess
            ? result
            : throw new ZeroAssertionException($"Expected the result to succeed, but {Explain.Errors(result.Errors)}");

    /// <summary>Asserts that the operation succeeded, and hands back what it produced.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <returns>The value the operation produced.</returns>
    /// <exception cref="ZeroAssertionException">It failed. The message lists every error.</exception>
    public static TValue ShouldSucceed<TValue>(this Result<TValue> result)
        => result.IsSuccess
            ? result.Value
            : throw new ZeroAssertionException($"Expected the result to succeed, but {Explain.Errors(result.Errors)}");

    /// <summary>Asserts that the operation failed, and hands back the reasons.</summary>
    /// <param name="result">The outcome under test.</param>
    /// <returns>Every reason it failed.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded.</exception>
    public static ErrorList ShouldFail(this Result result)
        => result.IsFailure
            ? result.Errors
            : throw new ZeroAssertionException("Expected the result to fail, but it succeeded.");

    /// <summary>Asserts that the operation failed, and hands back the reasons.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <returns>Every reason it failed.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded. The message shows the value.</exception>
    public static ErrorList ShouldFail<TValue>(this Result<TValue> result)
        => result.IsFailure
            ? result.Errors
            : throw new ZeroAssertionException(
                $"Expected the result to fail, but it succeeded with {Explain.Value(result.Value)}.");

    /// <summary>Asserts that the operation failed with a specific error code.</summary>
    /// <remarks>
    /// The code, not the message: the code is the part of the contract that is meant to stay
    /// stable, so a test written against it survives a reworded message.
    /// </remarks>
    /// <param name="result">The outcome under test.</param>
    /// <param name="code">The error code expected, conventionally <c>area.reason</c>.</param>
    /// <returns>The first error carrying that code.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or failed for other reasons.</exception>
    public static Error ShouldFailWith(this Result result, string code)
        => FindByCode(result.IsSuccess, result.Errors, code, null);

    /// <summary>Asserts that the operation failed with a specific error code.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <param name="code">The error code expected, conventionally <c>area.reason</c>.</param>
    /// <returns>The first error carrying that code.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or failed for other reasons.</exception>
    public static Error ShouldFailWith<TValue>(this Result<TValue> result, string code)
        => FindByCode(result.IsSuccess, result.Errors, code, result.IsSuccess ? Explain.Value(result.Value) : null);

    /// <summary>Asserts that the operation failed with a specific kind of error.</summary>
    /// <remarks>
    /// Use this where the test is about how the failure should be classified — a missing
    /// record must be <see cref="ErrorKind.NotFound"/> rather than a bare failure, because
    /// that is what decides the status code at the edge.
    /// </remarks>
    /// <param name="result">The outcome under test.</param>
    /// <param name="kind">The classification expected.</param>
    /// <returns>The first error of that kind.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or failed for other reasons.</exception>
    public static Error ShouldFailWith(this Result result, ErrorKind kind)
        => FindByKind(result.IsSuccess, result.Errors, kind, null);

    /// <summary>Asserts that the operation failed with a specific kind of error.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <param name="kind">The classification expected.</param>
    /// <returns>The first error of that kind.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or failed for other reasons.</exception>
    public static Error ShouldFailWith<TValue>(this Result<TValue> result, ErrorKind kind)
        => FindByKind(result.IsSuccess, result.Errors, kind, result.IsSuccess ? Explain.Value(result.Value) : null);

    /// <summary>Asserts that the operation failed with exactly these error codes, in any order.</summary>
    /// <remarks>
    /// For the case validation exists to serve: every reason at once. Order is not compared,
    /// because the order validators run in is not part of anyone's contract.
    /// </remarks>
    /// <param name="result">The outcome under test.</param>
    /// <param name="codes">Every code expected, and no others.</param>
    /// <returns>Every reason it failed.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or the set of codes differs.</exception>
    public static ErrorList ShouldFailWithCodes(this Result result, params string[] codes)
        => MatchCodes(result.IsSuccess, result.Errors, codes, null);

    /// <summary>Asserts that the operation failed with exactly these error codes, in any order.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <param name="codes">Every code expected, and no others.</param>
    /// <returns>Every reason it failed.</returns>
    /// <exception cref="ZeroAssertionException">It succeeded, or the set of codes differs.</exception>
    public static ErrorList ShouldFailWithCodes<TValue>(this Result<TValue> result, params string[] codes)
        => MatchCodes(result.IsSuccess, result.Errors, codes, result.IsSuccess ? Explain.Value(result.Value) : null);

    /// <summary>Asserts that the operation succeeded and its value satisfies a condition.</summary>
    /// <remarks>
    /// The condition's source text is captured by the compiler, so a failure reads
    /// <c>Expected the value to satisfy 'x =&gt; x.Total &gt; 0', but it was ...</c> without the
    /// test having to describe itself.
    /// </remarks>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <param name="predicate">What must hold for the value.</param>
    /// <param name="expression">Filled in by the compiler with the source text of <paramref name="predicate"/>.</param>
    /// <returns>The value the operation produced.</returns>
    /// <exception cref="ZeroAssertionException">It failed, or the value does not satisfy the condition.</exception>
    public static TValue ShouldHaveValue<TValue>(
        this Result<TValue> result,
        Func<TValue, bool> predicate,
        [CallerArgumentExpression(nameof(predicate))] string? expression = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (result.IsFailure)
            throw new ZeroAssertionException(
                $"Expected the value to satisfy {expression}, but {Explain.Errors(result.Errors)}");

        return predicate(result.Value)
            ? result.Value
            : throw new ZeroAssertionException(
                $"Expected the value to satisfy {expression}, but it was {Explain.Value(result.Value)}.");
    }

    /// <summary>Asserts that the operation succeeded and produced this value.</summary>
    /// <typeparam name="TValue">What the operation produces.</typeparam>
    /// <param name="result">The outcome under test.</param>
    /// <param name="expected">The value the operation should have produced.</param>
    /// <returns>The value the operation produced.</returns>
    /// <exception cref="ZeroAssertionException">It failed, or produced something else.</exception>
    public static TValue ShouldHaveValue<TValue>(this Result<TValue> result, TValue expected)
    {
        if (result.IsFailure)
            throw new ZeroAssertionException(
                $"Expected the value to be {Explain.Value(expected)}, but {Explain.Errors(result.Errors)}");

        return EqualityComparer<TValue>.Default.Equals(result.Value, expected)
            ? result.Value
            : throw new ZeroAssertionException(
                $"Expected the value to be {Explain.Value(expected)}, but it was {Explain.Value(result.Value)}.");
    }

    private static Error FindByCode(bool succeeded, ErrorList errors, string code, string? value)
    {
        if (succeeded)
            throw new ZeroAssertionException(
                $"Expected the result to fail with error code '{code}', but it succeeded"
                + (value is null ? "." : $" with {value}."));

        foreach (var error in errors)
            if (string.Equals(error.Code, code, StringComparison.Ordinal))
                return error;

        throw new ZeroAssertionException(
            $"Expected the result to fail with error code '{code}', but {Explain.Errors(errors)}");
    }

    private static Error FindByKind(bool succeeded, ErrorList errors, ErrorKind kind, string? value)
    {
        if (succeeded)
            throw new ZeroAssertionException(
                $"Expected the result to fail with a {kind} error, but it succeeded"
                + (value is null ? "." : $" with {value}."));

        foreach (var error in errors)
            if (error.Kind == kind)
                return error;

        throw new ZeroAssertionException(
            $"Expected the result to fail with a {kind} error, but {Explain.Errors(errors)}");
    }

    private static ErrorList MatchCodes(bool succeeded, ErrorList errors, string[] codes, string? value)
    {
        ArgumentNullException.ThrowIfNull(codes);

        if (succeeded)
            throw new ZeroAssertionException(
                $"Expected the result to fail with error codes {Explain.List(codes)}, but it succeeded"
                + (value is null ? "." : $" with {value}."));

        var actual = errors.Select(error => error.Code).ToList();
        var missing = codes.Except(actual, StringComparer.Ordinal).ToList();
        var unexpected = actual.Except(codes, StringComparer.Ordinal).ToList();

        if (missing.Count == 0 && unexpected.Count == 0 && actual.Count == codes.Length) return errors;

        var complaint = $"Expected the result to fail with error codes {Explain.List(codes)}, but {Explain.Errors(errors)}";

        if (missing.Count > 0) complaint += $"{Environment.NewLine}Missing: {Explain.List(missing)}.";
        if (unexpected.Count > 0) complaint += $"{Environment.NewLine}Unexpected: {Explain.List(unexpected)}.";

        throw new ZeroAssertionException(complaint);
    }
}
