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
/// A <see langword="default"/> instance is a failure carrying <see cref="Error.Uninitialised"/>.
/// That is deliberate: a struct that defaults to success would turn a forgotten assignment
/// into a silent pass. It carries a reason rather than nothing so that every failure — however
/// it was made — can be logged, mapped to a status and propagated the same way.
/// </para>
/// </remarks>
public readonly struct Result : IEquatable<Result>
{
    /// <summary>
    /// What a failure that states no reason of its own reports instead.
    /// </summary>
    /// <remarks>
    /// Shared rather than allocated per read: <see cref="Errors"/> is on every propagation
    /// path, and a default result is common enough to be worth not allocating for.
    /// </remarks>
    internal static readonly Error[] NoReasonGiven = [Error.Uninitialised];

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

    /// <summary>Every reason the operation failed. Empty on success, never empty on failure.</summary>
    public ErrorList Errors => new(IsSuccess ? null : _errors ?? NoReasonGiven);

    /// <summary>The first failure reason, or <see cref="Error.None"/> on success.</summary>
    public Error Error => Errors.Count > 0 ? Errors[0] : Error.None;

    /// <summary>A successful outcome.</summary>
    /// <returns>The result.</returns>
    public static Result Success() => new(true, null);

    /// <summary>
    /// A successful outcome carrying a value, with the type inferred.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The implicit conversion from a value cannot be used when the value's type is an
    /// interface: C# forbids a user-defined conversion whose source or target is one. That
    /// rules it out for the single most common thing a query handler returns —
    /// <c>IReadOnlyList&lt;T&gt;</c> — so <c>return page;</c> does not compile and the
    /// alternative is naming the whole closed type at the return.
    /// </para>
    /// <para>
    /// This infers it: <c>return Result.Success(page);</c>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TValue">What the operation produced. Inferred from the argument.</typeparam>
    /// <param name="value">What the operation produced.</param>
    /// <returns>The result.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => Result<TValue>.Success(value);

    /// <summary>A failed outcome.</summary>
    /// <param name="error">Why it failed.</param>
    /// <returns>The result.</returns>
    public static Result Failure(Error error) => new(false, [error]);

    /// <summary>A failed outcome with several reasons.</summary>
    /// <param name="errors">Why it failed. Must not be empty.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="errors"/> is empty.</exception>
    /// <remarks>
    /// Asking for a failure and naming no reason is a mistake in the caller, so it is reported
    /// here rather than absorbed. Propagation never lands here with nothing: a failure's
    /// <see cref="Errors"/> always has at least one entry.
    /// </remarks>
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
    public static Result Combine(params Result[] results) => Combine((IEnumerable<Result>)results);

    /// <summary>Succeeds when every result succeeded; otherwise collects all their errors.</summary>
    /// <param name="results">The outcomes to combine.</param>
    /// <returns>The combined result.</returns>
    /// <remarks>
    /// Whether the whole thing failed is decided by <see cref="IsFailure"/>, never by how many
    /// errors were gathered. Counting would let a failure that states no reason turn into a
    /// success, which is the one outcome this type exists to make impossible.
    /// </remarks>
    public static Result Combine(IEnumerable<Result> results)
    {
        var errors = new List<Error>();
        var failed = false;

        foreach (var result in results)
        {
            if (result.IsSuccess) continue;

            failed = true;
            errors.AddRange(result.Errors);
        }

        if (!failed) return Success();

        return errors.Count == 0 ? Failure(Error.Uninitialised) : Failure(errors);
    }

    /// <summary>Succeeds with every value when every result succeeded; otherwise collects all their errors.</summary>
    /// <typeparam name="TValue">What each operation produces.</typeparam>
    /// <param name="results">The outcomes to combine.</param>
    /// <returns>All the values, or all the reasons they are not all there.</returns>
    public static Result<TValue[]> Combine<TValue>(params Result<TValue>[] results)
        => Combine((IEnumerable<Result<TValue>>)results);

    /// <summary>Succeeds with every value when every result succeeded; otherwise collects all their errors.</summary>
    /// <typeparam name="TValue">What each operation produces.</typeparam>
    /// <param name="results">The outcomes to combine.</param>
    /// <returns>All the values, or all the reasons they are not all there.</returns>
    public static Result<TValue[]> Combine<TValue>(IEnumerable<Result<TValue>> results)
    {
        var values = new List<TValue>();
        var errors = new List<Error>();
        var failed = false;

        foreach (var result in results)
        {
            if (result.TryGetValue(out var value))
            {
                values.Add(value);
                continue;
            }

            failed = true;
            errors.AddRange(result.Errors);
        }

        if (!failed) return Result<TValue[]>.Success([.. values]);

        return errors.Count == 0
            ? Result<TValue[]>.Failure(Error.Uninitialised)
            : Result<TValue[]>.Failure(errors);
    }

    /// <summary>Reads the first reason only when the operation failed.</summary>
    /// <param name="error">Why it failed, when it did. <see cref="Error.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the operation failed.</returns>
    /// <remarks>The counterpart of <see cref="Result{TValue}.TryGetValue"/>: neither throws.</remarks>
    public bool TryGetError(out Error error)
    {
        error = Error;

        return IsFailure;
    }

    /// <summary>Carries this failure into a result that produces a value.</summary>
    /// <typeparam name="TValue">What the target result would have produced.</typeparam>
    /// <returns>The same failure, typed for the caller.</returns>
    /// <exception cref="InvalidOperationException">The operation succeeded, so there is no failure to carry.</exception>
    /// <remarks>
    /// Saves writing <c>Result&lt;T&gt;.Failure(result.Errors)</c> at every point where the
    /// type changes but the failure does not.
    /// </remarks>
    public Result<TValue> Cast<TValue>()
        => IsSuccess
            ? throw new InvalidOperationException(
                "Only a failure can be carried into another result type; this one succeeded.")
            : Result<TValue>.Failure(Errors);

    /// <summary>Turns an error into a failed result, so a method can <c>return someError;</c>.</summary>
    /// <param name="error">Why the operation failed.</param>
    public static implicit operator Result(Error error) => Failure(error);

    /// <summary>Turns reasons into a failed result, so a method can <c>return result.Errors;</c>.</summary>
    /// <param name="errors">Why the operation failed.</param>
    /// <remarks>
    /// An empty list produces <see cref="Error.Uninitialised"/> rather than throwing. This is a
    /// propagation path, and a propagation path that throws is how a failure becomes a 500.
    /// </remarks>
    public static implicit operator Result(ErrorList errors)
        => errors.Count == 0 ? Failure(Error.Uninitialised) : Failure(errors);

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
/// <remarks>
/// As with <see cref="Result"/>, a <see langword="default"/> instance is a failure — the one
/// an unassigned field, a <c>FirstOrDefault</c> over an empty sequence or a failed
/// <c>TryGetValue</c> hands you — and it carries <see cref="Error.Uninitialised"/> so that
/// propagating it works like propagating any other failure.
/// </remarks>
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

    /// <summary>Every reason the operation failed. Empty on success, never empty on failure.</summary>
    public ErrorList Errors => new(IsSuccess ? null : _errors ?? Result.NoReasonGiven);

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
        : throw new InvalidOperationException($"A failed result has no value. {Error}");

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
    /// <remarks>
    /// As on <see cref="Result"/>: naming no reason is a caller's mistake and is reported,
    /// while propagation cannot reach this state because a failure's <see cref="Errors"/> is
    /// never empty.
    /// </remarks>
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

    /// <summary>Reads the first reason only when the operation failed.</summary>
    /// <param name="error">Why it failed, when it did. <see cref="Error.None"/> otherwise.</param>
    /// <returns><see langword="true"/> when the operation failed.</returns>
    /// <remarks>
    /// The mirror image of <see cref="TryGetValue"/>, for the caller that is handling the
    /// failure rather than the value.
    /// </remarks>
    public bool TryGetError(out Error error)
    {
        error = Error;

        return IsFailure;
    }

    /// <summary>Carries this failure into a result that produces something else.</summary>
    /// <typeparam name="TOther">What the target result would have produced.</typeparam>
    /// <returns>The same failure, typed for the caller.</returns>
    /// <exception cref="InvalidOperationException">The operation succeeded, so there is no failure to carry.</exception>
    /// <remarks>
    /// Saves writing <c>Result&lt;TOther&gt;.Failure(result.Errors)</c> wherever a failure has
    /// to change type on its way out. Use <c>Map</c> when there is a value to convert.
    /// </remarks>
    public Result<TOther> Cast<TOther>()
        => IsSuccess
            ? throw new InvalidOperationException(
                "Only a failure can be carried into another result type; this one succeeded. Use Map instead.")
            : Result<TOther>.Failure(Errors);

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

    /// <summary>Turns reasons into a failed result, so a method can <c>return result.Errors;</c>.</summary>
    /// <param name="errors">Why the operation failed.</param>
    /// <remarks>
    /// An empty list produces <see cref="Error.Uninitialised"/> rather than throwing, for the
    /// same reason as on <see cref="Result"/>: propagation must not be able to fail.
    /// </remarks>
    public static implicit operator Result<TValue>(ErrorList errors)
        => errors.Count == 0 ? Failure(Error.Uninitialised) : Failure(errors);

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

/// <summary>
/// The reasons an operation failed. Empty when it succeeded.
/// </summary>
/// <remarks>
/// It prints itself as its reasons, because that is what the logging call in every one of
/// these guides does with it: <c>logger.LogWarning("... {Errors}", result.Errors)</c> ends up
/// calling <see cref="object.ToString"/>, and a log line reading <c>IQOne.Zero.ErrorList</c>
/// costs the same to write as the one that says what went wrong.
/// </remarks>
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

    /// <inheritdoc />
    public override string ToString() => _errors is null or [] ? "(none)" : string.Join(" | ", _errors);
}
