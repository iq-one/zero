using System.Text.RegularExpressions;

namespace IQOne.Zero.Validation;

/// <summary>
/// Collects the rules a validator applies.
/// </summary>
/// <remarks>
/// Each rule states its own error code rather than deriving one from the property. The code
/// is part of the published contract — callers branch on it and translators key off it — so
/// renaming a property must not change it.
/// </remarks>
/// <typeparam name="T">The value checked.</typeparam>
public sealed class RuleSet<T>
{
    private readonly List<Func<T, CancellationToken, ValueTask<Error?>>> _rules = [];

    private Func<T, bool>? _condition;

    /// <summary>How many rules have been added.</summary>
    public int Count => _rules.Count;

    /// <summary>Applies the rules added inside <paramref name="rules"/> only when the condition holds.</summary>
    /// <param name="condition">Decides whether the nested rules apply.</param>
    /// <param name="rules">Adds the conditional rules.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> When(Func<T, bool> condition, Action<RuleSet<T>> rules)
    {
        var outer = _condition;

        _condition = outer is null ? condition : value => outer(value) && condition(value);
        rules(this);
        _condition = outer;

        return this;
    }

    /// <summary>Fails when the text is null, empty or only whitespace.</summary>
    /// <param name="select">Reads the text.</param>
    /// <param name="code">Stable error code, conventionally <c>area.field</c>.</param>
    /// <param name="message">What is wrong. A default is used when omitted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> NotEmpty(Func<T, string?> select, string code, string? message = null)
        => Must(value => !string.IsNullOrWhiteSpace(select(value)), code, message ?? "A value is required.");

    /// <summary>Fails when the collection is null or has no items.</summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="select">Reads the collection.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">What is wrong. A default is used when omitted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> NotEmpty<TItem>(Func<T, IEnumerable<TItem>?> select, string code, string? message = null)
        => Must(value => select(value)?.Any() == true, code, message ?? "At least one item is required.");

    /// <summary>Fails when the value is null.</summary>
    /// <typeparam name="TValue">The value's type.</typeparam>
    /// <param name="select">Reads the value.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">What is wrong. A default is used when omitted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> NotNull<TValue>(Func<T, TValue?> select, string code, string? message = null)
        => Must(value => select(value) is not null, code, message ?? "A value is required.");

    /// <summary>Fails when the text is outside the given length range. Null passes; pair it with <see cref="NotEmpty(Func{T,string},string,string)"/>.</summary>
    /// <param name="select">Reads the text.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="minimum">Shortest acceptable length.</param>
    /// <param name="maximum">Longest acceptable length.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> Length(Func<T, string?> select, string code, int minimum, int maximum)
        => Must(
            value => select(value) is not { } text || (text.Length >= minimum && text.Length <= maximum),
            code,
            $"The length must be between {minimum} and {maximum} characters.");

    /// <summary>Fails when the value is outside the given range, inclusive.</summary>
    /// <typeparam name="TValue">The value's type.</typeparam>
    /// <param name="select">Reads the value.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="minimum">Smallest acceptable value.</param>
    /// <param name="maximum">Largest acceptable value.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> InRange<TValue>(Func<T, TValue> select, string code, TValue minimum, TValue maximum)
        where TValue : IComparable<TValue>
        => Must(
            value => select(value).CompareTo(minimum) >= 0 && select(value).CompareTo(maximum) <= 0,
            code,
            $"The value must be between {minimum} and {maximum}.");

    /// <summary>Fails when the text does not match the pattern. Null passes.</summary>
    /// <param name="select">Reads the text.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="pattern">The pattern the text must match.</param>
    /// <param name="message">What is wrong. A default is used when omitted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> Matches(Func<T, string?> select, string code, Regex pattern, string? message = null)
        => Must(
            value => select(value) is not { } text || pattern.IsMatch(text),
            code,
            message ?? "The value is not in the expected format.");

    /// <summary>Fails when the condition does not hold.</summary>
    /// <param name="predicate">Must hold for the value to be acceptable.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">What is wrong and what would be accepted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> Must(Func<T, bool> predicate, string code, string message)
    {
        var condition = _condition;

        _rules.Add((value, _) => new ValueTask<Error?>(
            condition?.Invoke(value) == false || predicate(value)
                ? null
                : Error.Validation(code, message)));

        return this;
    }

    /// <summary>
    /// Fails when the condition does not hold, where deciding needs a dependency.
    /// </summary>
    /// <remarks>
    /// For checks that must reach out — a name that has to be unique, a code that has to
    /// exist. Keep these few: each one is a round trip taken before the handler runs.
    /// </remarks>
    /// <param name="predicate">Must hold for the value to be acceptable.</param>
    /// <param name="code">Stable error code.</param>
    /// <param name="message">What is wrong and what would be accepted.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> MustAsync(
        Func<T, CancellationToken, ValueTask<bool>> predicate, string code, string message)
    {
        var condition = _condition;

        _rules.Add(async (value, cancellationToken) =>
            condition?.Invoke(value) == false || await predicate(value, cancellationToken).ConfigureAwait(false)
                ? null
                : Error.Validation(code, message));

        return this;
    }

    /// <summary>Applies a nested validator to each item of a collection.</summary>
    /// <typeparam name="TItem">The item type.</typeparam>
    /// <param name="select">Reads the collection. Null is treated as empty.</param>
    /// <param name="validator">Checks one item.</param>
    /// <returns>This set, for chaining.</returns>
    public RuleSet<T> ForEach<TItem>(Func<T, IEnumerable<TItem>?> select, IValidator<TItem> validator)
    {
        var condition = _condition;

        _rules.Add(async (value, cancellationToken) =>
        {
            if (condition?.Invoke(value) == false) return null;

            foreach (var item in select(value) ?? [])
            {
                var errors = await validator.ValidateAsync(item, cancellationToken).ConfigureAwait(false);

                if (errors.Count > 0) return errors[0];
            }

            return null;
        });

        return this;
    }

    /// <summary>Runs every rule and collects every failure.</summary>
    /// <param name="value">What to check.</param>
    /// <param name="cancellationToken">Cancels a rule that reaches a dependency.</param>
    /// <returns>Every reason the value is unacceptable.</returns>
    internal async ValueTask<IReadOnlyList<Error>> RunAsync(T value, CancellationToken cancellationToken)
    {
        List<Error>? errors = null;

        foreach (var rule in _rules)
        {
            var error = await rule(value, cancellationToken).ConfigureAwait(false);

            if (error is not null) (errors ??= []).Add(error.Value);
        }

        return errors ?? (IReadOnlyList<Error>)[];
    }
}
