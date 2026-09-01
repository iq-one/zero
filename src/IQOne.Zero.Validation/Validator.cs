namespace IQOne.Zero.Validation;

/// <summary>
/// Base for a validator: state the rules once, and they are applied on every request.
/// </summary>
/// <remarks>
/// Rules are built in the constructor, so a validator may take dependencies and use them in
/// <see cref="RuleSet{T}.MustAsync"/>. Nothing is compiled or reflected over at run time —
/// each rule is a plain delegate the C# compiler already produced.
/// </remarks>
/// <typeparam name="T">The value checked.</typeparam>
public abstract class Validator<T> : IValidator<T>
{
    private readonly RuleSet<T> _rules = new();

    /// <summary>Builds the rule set. Called once, when the validator is constructed.</summary>
    protected Validator() => Configure(_rules);

    /// <summary>States the rules for <typeparamref name="T"/>.</summary>
    /// <param name="rules">The set to add rules to.</param>
    protected abstract void Configure(RuleSet<T> rules);

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<Error>> ValidateAsync(T value, CancellationToken cancellationToken)
        => _rules.RunAsync(value, cancellationToken);
}
