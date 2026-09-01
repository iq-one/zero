using IQOne.Zero.Validation;

namespace IQOne.Zero.Testing;

/// <summary>
/// Assertions on a validator, for the tests that check its rules directly.
/// </summary>
/// <remarks>
/// A validator is worth testing on its own: the rules are the interesting part, and there is
/// nothing to be learned by sending a request through a pipeline once per rule. Whether the
/// pipeline actually applies the validator is one test, not one per case — use
/// <see cref="ZeroTestApplication"/> or <see cref="HandlerHarness{TRequest,TResponse}"/> for
/// that one.
/// </remarks>
public static class ValidatorAssertions
{
    /// <summary>Asserts that the validator finds nothing wrong with the value.</summary>
    /// <typeparam name="T">What the validator checks.</typeparam>
    /// <param name="validator">The validator under test.</param>
    /// <param name="value">The value it should accept.</param>
    /// <param name="cancellationToken">Cancels a rule that reaches a dependency.</param>
    /// <returns>A task that completes when the assertion holds.</returns>
    /// <exception cref="ZeroAssertionException">The validator rejected the value, and the message says why.</exception>
    public static async Task ShouldAcceptAsync<T>(
        this IValidator<T> validator, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var errors = await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);

        if (errors.Count == 0) return;

        throw new ZeroAssertionException(
            $"Expected {validator.GetType().Name} to accept {Explain.Value(value)}, but it reported " +
            $"{errors.Count} {(errors.Count == 1 ? "error" : "errors")}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, errors.Select((error, index) => $"  [{index + 1}] {error}")));
    }

    /// <summary>Asserts that the validator rejects the value, and hands back every reason.</summary>
    /// <typeparam name="T">What the validator checks.</typeparam>
    /// <param name="validator">The validator under test.</param>
    /// <param name="value">The value it should reject.</param>
    /// <param name="cancellationToken">Cancels a rule that reaches a dependency.</param>
    /// <returns>Every reason the value is unacceptable.</returns>
    /// <exception cref="ZeroAssertionException">The validator accepted the value.</exception>
    public static async Task<IReadOnlyList<Error>> ShouldRejectAsync<T>(
        this IValidator<T> validator, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var errors = await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);

        return errors.Count > 0
            ? errors
            : throw new ZeroAssertionException(
                $"Expected {validator.GetType().Name} to reject {Explain.Value(value)}, but it found nothing wrong.");
    }

    /// <summary>Asserts that the validator rejects the value with a specific error code.</summary>
    /// <remarks>
    /// The code rather than the message, for the same reason a caller branches on the code:
    /// it is the part of the contract that is meant to survive rewording.
    /// </remarks>
    /// <typeparam name="T">What the validator checks.</typeparam>
    /// <param name="validator">The validator under test.</param>
    /// <param name="value">The value it should reject.</param>
    /// <param name="code">The error code expected.</param>
    /// <param name="cancellationToken">Cancels a rule that reaches a dependency.</param>
    /// <returns>The first error carrying that code.</returns>
    /// <exception cref="ZeroAssertionException">It accepted the value, or rejected it for other reasons.</exception>
    public static async Task<Error> ShouldRejectAsync<T>(
        this IValidator<T> validator, T value, string code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var errors = await validator.ValidateAsync(value, cancellationToken).ConfigureAwait(false);

        foreach (var error in errors)
            if (string.Equals(error.Code, code, StringComparison.Ordinal))
                return error;

        throw new ZeroAssertionException(
            $"Expected {validator.GetType().Name} to reject {Explain.Value(value)} with error code '{code}', but "
            + (errors.Count == 0
                ? "it found nothing wrong."
                : $"it reported {Explain.List(errors.Select(error => error.Code))}."));
    }
}
