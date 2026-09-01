using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace IQOne.Zero;

/// <summary>
/// The outcome of an operation that is expected to fail sometimes.
/// </summary>
/// <remarks>
/// <para>
/// Returning a result rather than throwing makes the failure part of the signature, so a
/// caller cannot overlook it by accident and a reader can see it without opening the body.
/// Exceptions remain for the unexpected: a bug, a broken invariant, a machine in trouble.
/// </para>
/// <para>
/// A <see langword="default"/> instance is a failure carrying <see cref="Error.None"/>. That
/// is deliberate: a struct that defaults to success would turn a forgotten assignment into a
/// silent pass.
/// </para>
/// </remarks>
public readonly struct Result : IEquatable<Result>
{
    private readonly Error[]? _errors;

    private Result(bool succeeded, Error[]? errors)
    {
        IsSuccess = succeeded;
        _errors = errors;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Every reason the operation failed. Empty on success.</summary>
    public ErrorList Errors => new(IsSuccess ? null : _errors);

    /// <summary>The first failure reason, or <see cref="Error.None"/> on success.</summary>
    public Error Error => Errors.Count > 0 ? Errors[0] : Error.None;

    /// <summary>A successful outcome.</summary>
    /// <returns>The result.</returns>
    public static Result Success() => new(true, null);

    /// <summary>A failed outcome.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The result.</returns>
    public static Result Failure(Error error) => new(false, [error]);

    /// <summary>A failed outcome with several reasons.</summary>
    /// <param name="errors">Why it failed. Must not be empty.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    public static Result Failure(IEnumerable<Error> errors)
    {
        var collected = errors.ToArray();

        if (collected.Length == 0)
            throw new ArgumentException("A failed result needs at least one error.", nameof(errors));

        return new Result(false, collected);
    }

    /// <summary>Succeeds when every result succeeded; otherwise collects all their errors.</summary>
    /// <param name="results">The outcomes to combine.</param>
    /// <returns>The combined result.</returns>
    public static Result Combine(params Result[] results)
    {
        var errors = results.Where(r => r.IsFailure).SelectMany(r => r.Errors).ToArray();

        return errors.Length == 0 ? Success() : Failure(errors);
    }

    /// <summary>Turns an error into a failed result, so a method can <c>return someError;</c>.</summary>
    /// <param name="error">Why the operation failed.</param>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Runs whichever branch matches the outcome.</summary>
    /// <typeparam name="TOut">What both branches produce.</typeparam>
    /// <param name="onSuccess">Runs when the operation succeeded.</param>
    /// <param name="onFailure">Runs when it failed, with every reason.</param>
    /// <returns>Whatever the branch that ran produced.</returns>
    public TOut Match<TOut>(Func<TOut> onSuccess, Func<ErrorList, TOut> onFailure)
        => IsSuccess ? onSuccess() : onFailure(Errors);

    /// <inheritdoc />
    public bool Equals(Result other) => IsSuccess == other.IsSuccess && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsSuccess, Errors.Count);

    /// <summary>Whether two outcomes are the same.</summary>
    public static bool operator ==(Result left, Result right) => left.Equals(right);

    /// <summary>Whether two outcomes differ.</summary>
    public static bool operator !=(Result left, Result right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsSuccess ? "Success" : $"Failure({Errors.Count}): {Error}";
}

/// <summary>
/// The outcome of an operation that produces a value and is expected to fail sometimes.
/// </summary>
/// <typeparam name="TValue">What the operation produces when it succeeds.</typeparam>
public readonly struct Result<TValue> : IEquatable<Result<TValue>>
{
    private readonly TValue? _value;
    private readonly Error[]? _errors;

    private Result(bool succeeded, TValue? value, Error[]? errors)
    {
        IsSuccess = succeeded;
        _value = value;
        _errors = errors;
    }

    /// <summary>Whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Every reason the operation failed. Empty on success.</summary>
    public ErrorList Errors => new(IsSuccess ? null : _errors);

    /// <summary>The first failure reason, or <see cref="Error.None"/> on success.</summary>
    public Error Error => Errors.Count > 0 ? Errors[0] : Error.None;

    /// <summary>
    /// What the operation produced.
    /// </summary>
    /// <remarks>
    /// Prefer <see cref="Match{TOut}"/> or <see cref="TryGetValue"/>: both make the failing
    /// branch impossible to forget. Reading this without checking first is reported as
    /// ZERO101.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The operation failed.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            Errors.Count == 0
                ? "This result was never initialised, so it carries no value. A default Result<T> is a failure."
                : $"A failed result has no value. {Error}");

    /// <summary>A successful outcome carrying a value.</summary>
    /// <param name="value">What the operation produced.</param>
    /// <returns>The result.</returns>
    public static Result<TValue> Success(TValue value) => new(true, value, null);

    /// <summary>A failed outcome.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The result.</returns>
    public static Result<TValue> Failure(Error error) => new(false, default, [error]);

    /// <summary>A failed outcome with several reasons.</summary>
    /// <param name="errors">Why it failed. Must not be empty.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    public static Result<TValue> Failure(IEnumerable<Error> errors)
    {
        var collected = errors.ToArray();

        if (collected.Length == 0)
            throw new ArgumentException("A failed result needs at least one error.", nameof(errors));

        return new Result<TValue>(false, default, collected);
    }

    /// <summary>Reads the value only when the operation succeeded.</summary>
    /// <param name="value">What the operation produced, when it succeeded.</param>
    /// <returns><see langword="true"/> when the operation succeeded.</returns>
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>Runs whichever branch matches the outcome.</summary>
    /// <typeparam name="TOut">What both branches produce.</typeparam>
    /// <param name="onSuccess">Runs with the value when the operation succeeded.</param>
    /// <param name="onFailure">Runs with every reason when it failed.</param>
    /// <returns>Whatever the branch that ran produced.</returns>
    public TOut Match<TOut>(Func<TValue, TOut> onSuccess, Func<ErrorList, TOut> onFailure)
        => IsSuccess ? onSuccess(_value!) : onFailure(Errors);

    /// <summary>Turns a value into a successful result, so a method can <c>return value;</c>.</summary>
    /// <param name="value">What the operation produced.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Turns an error into a failed result, so a method can <c>return someError;</c>.</summary>
    /// <param name="error">Why the operation failed.</param>
    public static implicit operator Result<TValue>(Error error) => Failure(error);

    /// <summary>Discards the value, keeping only whether the operation succeeded.</summary>
    /// <param name="result">The outcome to narrow.</param>
    public static implicit operator Result(Result<TValue> result)
        => result.IsSuccess ? Result.Success() : Result.Failure(result.Errors);

    /// <inheritdoc />
    public bool Equals(Result<TValue> other)
        => IsSuccess == other.IsSuccess
        && EqualityComparer<TValue?>.Default.Equals(_value, other._value)
        && Errors.SequenceEqual(other.Errors);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result<TValue> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(IsSuccess, _value, Errors.Count);

    /// <summary>Whether two outcomes are the same.</summary>
    public static bool operator ==(Result<TValue> left, Result<TValue> right) => left.Equals(right);

    /// <summary>Whether two outcomes differ.</summary>
    public static bool operator !=(Result<TValue> left, Result<TValue> right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Errors.Count}): {Error}";
}

/// <summary>The reasons an operation failed. Empty when it succeeded.</summary>
public readonly struct ErrorList : IReadOnlyList<Error>
{
    private readonly Error[]? _errors;

    internal ErrorList(Error[]? errors) => _errors = errors;

    /// <summary>How many reasons there are.</summary>
    public int Count => _errors?.Length ?? 0;

    /// <summary>The reason at this position.</summary>
    /// <param name="index">Zero-based position.</param>
    public Error this[int index] => _errors is null
        ? throw new ArgumentOutOfRangeException(nameof(index), "This result carries no errors.")
        : _errors[index];

    /// <inheritdoc />
    public IEnumerator<Error> GetEnumerator() => ((IEnumerable<Error>)(_errors ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
