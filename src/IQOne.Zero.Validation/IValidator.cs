using IQOne.Zero.DependencyInjection.Descriptors;

namespace IQOne.Zero.Validation;

/// <summary>Marker the generator uses to find validators. Do not implement it directly.</summary>
public interface IValidator : IScoped;

/// <summary>
/// Checks that a value is acceptable, returning every reason it is not.
/// </summary>
/// <remarks>
/// <para>
/// Every reason, not the first: a caller fixing a form wants the whole list, and returning
/// them one at a time turns a single correction into several round trips.
/// </para>
/// <para>
/// Validation belongs here rather than in the handler. A handler that validates has to be
/// trusted to do it — and the next handler, and the one after that. A validator is found by
/// the generator and run by the pipeline, so it cannot be skipped.
/// </para>
/// </remarks>
/// <typeparam name="T">The value checked.</typeparam>
public interface IValidator<in T> : IValidator
{
    /// <summary>Checks the value.</summary>
    /// <param name="value">What to check.</param>
    /// <param name="cancellationToken">Cancels any rule that reaches a dependency.</param>
    /// <returns>Every reason the value is unacceptable. Empty when it is fine.</returns>
    ValueTask<IReadOnlyList<Error>> ValidateAsync(T value, CancellationToken cancellationToken);
}
