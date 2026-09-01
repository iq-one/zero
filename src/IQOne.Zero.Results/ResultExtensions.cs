namespace IQOne.Zero;

/// <summary>
/// Composes results without unpacking them.
/// </summary>
/// <remarks>
/// Each of these short-circuits on failure, so a chain reads as the happy path while still
/// handling every failure. That is the point: the alternative is a check after every call,
/// which is exactly the code people stop writing when they are in a hurry.
/// </remarks>
public static class ResultExtensions
{
    /// <summary>Transforms the value when the operation succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the transform produces.</typeparam>
    /// <param name="result">The outcome to transform.</param>
    /// <param name="map">Applied to the value on success. Cannot itself fail; use <c>Bind</c> if it can.</param>
    /// <returns>The transformed outcome, or the original failure.</returns>
    public static Result<TOut> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, TOut> map)
        => result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : Result<TOut>.Failure(result.Errors);

    /// <summary>Runs the next operation only when this one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> bind)
        => result.IsSuccess ? bind(result.Value) : Result<TOut>.Failure(result.Errors);

    /// <summary>Runs the next operation only when this one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static Result Bind<TIn>(this Result<TIn> result, Func<TIn, Result> bind)
        => result.IsSuccess ? bind(result.Value) : Result.Failure(result.Errors);

    /// <summary>Fails when the value does not satisfy the condition.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to check.</param>
    /// <param name="predicate">Must hold for the outcome to stay successful.</param>
    /// <param name="error">Reported when the predicate does not hold.</param>
    /// <returns>The original outcome, or a failure carrying <paramref name="error"/>.</returns>
    public static Result<TValue> Ensure<TValue>(this Result<TValue> result, Func<TValue, bool> predicate, Error error)
        => result.IsFailure ? result
            : predicate(result.Value) ? result
            : Result<TValue>.Failure(error);

    /// <summary>Runs a side effect on success and passes the outcome through unchanged.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs with the value on success.</param>
    /// <returns>The original outcome.</returns>
    public static Result<TValue> Tap<TValue>(this Result<TValue> result, Action<TValue> action)
    {
        if (result.IsSuccess) action(result.Value);

        return result;
    }

    /// <summary>Runs a side effect on failure and passes the outcome through unchanged.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs with every reason on failure.</param>
    /// <returns>The original outcome.</returns>
    public static Result<TValue> TapError<TValue>(this Result<TValue> result, Action<ErrorList> action)
    {
        if (result.IsFailure) action(result.Errors);

        return result;
    }

    /// <summary>The value on success, or the given fallback on failure.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to read.</param>
    /// <param name="fallback">Returned on failure.</param>
    /// <returns>The value, or the fallback.</returns>
    public static TValue GetValueOr<TValue>(this Result<TValue> result, TValue fallback)
        => result.IsSuccess ? result.Value : fallback;

    /// <summary>The value on success, or a fallback computed from the errors.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to read.</param>
    /// <param name="fallback">Produces the value to use on failure.</param>
    /// <returns>The value, or the fallback.</returns>
    public static TValue GetValueOr<TValue>(this Result<TValue> result, Func<ErrorList, TValue> fallback)
        => result.IsSuccess ? result.Value : fallback(result.Errors);

    // ---- asynchronous forms -------------------------------------------------------------
    //
    // Present so that a chain does not have to be broken apart the moment one step is
    // asynchronous. Every one awaits the antecedent first, so ordering is what it reads like.

    /// <summary>Transforms the value when the awaited operation succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the transform produces.</typeparam>
    /// <param name="result">The outcome to transform.</param>
    /// <param name="map">Applied to the value on success.</param>
    /// <returns>The transformed outcome, or the original failure.</returns>
    public static async Task<Result<TOut>> Map<TIn, TOut>(this Task<Result<TIn>> result, Func<TIn, TOut> map)
        => (await result.ConfigureAwait(false)).Map(map);

    /// <summary>Runs the next operation only when the awaited one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> result, Func<TIn, Task<Result<TOut>>> bind)
    {
        var awaited = await result.ConfigureAwait(false);

        return awaited.IsSuccess
            ? await bind(awaited.Value).ConfigureAwait(false)
            : Result<TOut>.Failure(awaited.Errors);
    }

    /// <summary>Runs the next operation only when this one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Result<TIn> result, Func<TIn, Task<Result<TOut>>> bind)
        => result.IsSuccess
            ? await bind(result.Value).ConfigureAwait(false)
            : Result<TOut>.Failure(result.Errors);

    /// <summary>Runs whichever branch matches the awaited outcome.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <typeparam name="TOut">What both branches produce.</typeparam>
    /// <param name="result">The outcome to match on.</param>
    /// <param name="onSuccess">Runs with the value on success.</param>
    /// <param name="onFailure">Runs with every reason on failure.</param>
    /// <returns>Whatever the branch that ran produced.</returns>
    public static async Task<TOut> Match<TValue, TOut>(
        this Task<Result<TValue>> result, Func<TValue, TOut> onSuccess, Func<ErrorList, TOut> onFailure)
        => (await result.ConfigureAwait(false)).Match(onSuccess, onFailure);
}
