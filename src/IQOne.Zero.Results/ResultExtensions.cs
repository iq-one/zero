namespace IQOne.Zero;

/// <summary>
/// Composes results without unpacking them.
/// </summary>
/// <remarks>
/// <para>
/// Each of these short-circuits on failure, so a chain reads as the happy path while still
/// handling every failure. That is the point: the alternative is a check after every call,
/// which is exactly the code people stop writing when they are in a hurry.
/// </para>
/// <para>
/// Every operation exists in four shapes where it makes sense — synchronous, on an awaited
/// result, with an asynchronous step, and on the valueless <see cref="Result"/>. A missing
/// shape is not a small gap: it forces the chain to be broken open into locals at that step,
/// and once it is open people stop putting it back together.
/// </para>
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
        => result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : result.Cast<TOut>();

    /// <summary>Runs the next operation only when this one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static Result<TOut> Bind<TIn, TOut>(this Result<TIn> result, Func<TIn, Result<TOut>> bind)
        => result.IsSuccess ? bind(result.Value) : result.Cast<TOut>();

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

    /// <summary>Rewrites every reason while keeping the outcome a failure.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="map">Applied to each reason on failure.</param>
    /// <returns>The original success, or the failure with its reasons rewritten.</returns>
    /// <remarks>
    /// For the boundary where an inner failure means something different to the caller: a
    /// storage <c>NotFound</c> that is really a <c>Conflict</c> here, or a code from another
    /// layer that should not leak out with that name.
    /// </remarks>
    public static Result<TValue> MapError<TValue>(this Result<TValue> result, Func<Error, Error> map)
        => result.IsSuccess ? result : Result<TValue>.Failure(result.Errors.Select(map));

    /// <summary>Rewrites every reason while keeping the outcome a failure.</summary>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="map">Applied to each reason on failure.</param>
    /// <returns>The original success, or the failure with its reasons rewritten.</returns>
    public static Result MapError(this Result result, Func<Error, Error> map)
        => result.IsSuccess ? result : Result.Failure(result.Errors.Select(map));

    /// <summary>Replaces the reasons of a failure with one of your own.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="error">What to report instead.</param>
    /// <returns>The original success, or a failure carrying <paramref name="error"/>.</returns>
    /// <remarks>
    /// Use when the reason underneath is an implementation detail the caller should not see.
    /// Prefer <c>MapError</c> when the original reasons are still worth carrying.
    /// </remarks>
    public static Result<TValue> WithError<TValue>(this Result<TValue> result, Error error)
        => result.IsSuccess ? result : Result<TValue>.Failure(error);

    /// <summary>Replaces the reasons of a failure with one of your own.</summary>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="error">What to report instead.</param>
    /// <returns>The original success, or a failure carrying <paramref name="error"/>.</returns>
    public static Result WithError(this Result result, Error error)
        => result.IsSuccess ? result : Result.Failure(error);

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

    // ---- the valueless Result ------------------------------------------------------------
    //
    // A command handler returns Result<Unit>, and everything it calls tends to return Result.
    // Without these the void case has no composition story at all, so it gets written as a
    // stack of `if (x.IsFailure) return x;` — the shape this package exists to replace.

    /// <summary>Runs the next operation only when this one succeeded.</summary>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static Result Bind(this Result result, Func<Result> bind)
        => result.IsSuccess ? bind() : result;

    /// <summary>Runs the next operation, which produces a value, only when this one succeeded.</summary>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static Result<TOut> Bind<TOut>(this Result result, Func<Result<TOut>> bind)
        => result.IsSuccess ? bind() : result.Cast<TOut>();

    /// <summary>Runs a side effect on success and passes the outcome through unchanged.</summary>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs on success.</param>
    /// <returns>The original outcome.</returns>
    public static Result Tap(this Result result, Action action)
    {
        if (result.IsSuccess) action();

        return result;
    }

    /// <summary>Runs a side effect on failure and passes the outcome through unchanged.</summary>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs with every reason on failure.</param>
    /// <returns>The original outcome.</returns>
    public static Result TapError(this Result result, Action<ErrorList> action)
    {
        if (result.IsFailure) action(result.Errors);

        return result;
    }

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

    /// <summary>Transforms the value with an asynchronous step when the operation succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the transform produces.</typeparam>
    /// <param name="result">The outcome to transform.</param>
    /// <param name="map">Applied to the value on success. Cannot itself fail; use <c>Bind</c> if it can.</param>
    /// <returns>The transformed outcome, or the original failure.</returns>
    public static async Task<Result<TOut>> Map<TIn, TOut>(this Result<TIn> result, Func<TIn, Task<TOut>> map)
        => result.IsSuccess
            ? Result<TOut>.Success(await map(result.Value).ConfigureAwait(false))
            : result.Cast<TOut>();

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
            : awaited.Cast<TOut>();
    }

    /// <summary>Runs a synchronous next operation only when the awaited one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <typeparam name="TOut">What the next operation produces.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result<TOut>> Bind<TIn, TOut>(
        this Task<Result<TIn>> result, Func<TIn, Result<TOut>> bind)
        => (await result.ConfigureAwait(false)).Bind(bind);

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
            : result.Cast<TOut>();

    /// <summary>Runs the next operation, which produces nothing, only when the awaited one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result> Bind<TIn>(this Task<Result<TIn>> result, Func<TIn, Task<Result>> bind)
    {
        var awaited = await result.ConfigureAwait(false);

        return awaited.IsSuccess
            ? await bind(awaited.Value).ConfigureAwait(false)
            : Result.Failure(awaited.Errors);
    }

    /// <summary>Runs a synchronous next operation, which produces nothing, only when the awaited one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result> Bind<TIn>(this Task<Result<TIn>> result, Func<TIn, Result> bind)
        => (await result.ConfigureAwait(false)).Bind(bind);

    /// <summary>Runs the next operation, which produces nothing, only when this one succeeded.</summary>
    /// <typeparam name="TIn">The value produced.</typeparam>
    /// <param name="result">The outcome to continue from.</param>
    /// <param name="bind">The next operation, which may itself fail.</param>
    /// <returns>The next outcome, or the original failure.</returns>
    public static async Task<Result> Bind<TIn>(this Result<TIn> result, Func<TIn, Task<Result>> bind)
        => result.IsSuccess
            ? await bind(result.Value).ConfigureAwait(false)
            : Result.Failure(result.Errors);

    /// <summary>Fails when the awaited value does not satisfy the condition.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to check.</param>
    /// <param name="predicate">Must hold for the outcome to stay successful.</param>
    /// <param name="error">Reported when the predicate does not hold.</param>
    /// <returns>The original outcome, or a failure carrying <paramref name="error"/>.</returns>
    public static async Task<Result<TValue>> Ensure<TValue>(
        this Task<Result<TValue>> result, Func<TValue, bool> predicate, Error error)
        => (await result.ConfigureAwait(false)).Ensure(predicate, error);

    /// <summary>Runs a side effect on an awaited success and passes the outcome through unchanged.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs with the value on success.</param>
    /// <returns>The original outcome.</returns>
    public static async Task<Result<TValue>> Tap<TValue>(this Task<Result<TValue>> result, Action<TValue> action)
        => (await result.ConfigureAwait(false)).Tap(action);

    /// <summary>Runs a side effect on an awaited failure and passes the outcome through unchanged.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to observe.</param>
    /// <param name="action">Runs with every reason on failure.</param>
    /// <returns>The original outcome.</returns>
    public static async Task<Result<TValue>> TapError<TValue>(
        this Task<Result<TValue>> result, Action<ErrorList> action)
        => (await result.ConfigureAwait(false)).TapError(action);

    /// <summary>Rewrites every reason of an awaited failure.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="map">Applied to each reason on failure.</param>
    /// <returns>The original success, or the failure with its reasons rewritten.</returns>
    public static async Task<Result<TValue>> MapError<TValue>(
        this Task<Result<TValue>> result, Func<Error, Error> map)
        => (await result.ConfigureAwait(false)).MapError(map);

    /// <summary>Replaces the reasons of an awaited failure with one of your own.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to re-tag.</param>
    /// <param name="error">What to report instead.</param>
    /// <returns>The original success, or a failure carrying <paramref name="error"/>.</returns>
    public static async Task<Result<TValue>> WithError<TValue>(this Task<Result<TValue>> result, Error error)
        => (await result.ConfigureAwait(false)).WithError(error);

    /// <summary>The awaited value on success, or the given fallback on failure.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to read.</param>
    /// <param name="fallback">Returned on failure.</param>
    /// <returns>The value, or the fallback.</returns>
    public static async Task<TValue> GetValueOr<TValue>(this Task<Result<TValue>> result, TValue fallback)
        => (await result.ConfigureAwait(false)).GetValueOr(fallback);

    /// <summary>The awaited value on success, or a fallback computed from the errors.</summary>
    /// <typeparam name="TValue">The value produced.</typeparam>
    /// <param name="result">The outcome to read.</param>
    /// <param name="fallback">Produces the value to use on failure.</param>
    /// <returns>The value, or the fallback.</returns>
    public static async Task<TValue> GetValueOr<TValue>(
        this Task<Result<TValue>> result, Func<ErrorList, TValue> fallback)
        => (await result.ConfigureAwait(false)).GetValueOr(fallback);

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

    /// <summary>Runs whichever branch matches the awaited outcome.</summary>
    /// <typeparam name="TOut">What both branches produce.</typeparam>
    /// <param name="result">The outcome to match on.</param>
    /// <param name="onSuccess">Runs when the operation succeeded.</param>
    /// <param name="onFailure">Runs with every reason on failure.</param>
    /// <returns>Whatever the branch that ran produced.</returns>
    public static async Task<TOut> Match<TOut>(
        this Task<Result> result, Func<TOut> onSuccess, Func<ErrorList, TOut> onFailure)
        => (await result.ConfigureAwait(false)).Match(onSuccess, onFailure);
}
