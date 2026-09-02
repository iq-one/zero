using IQOne.Zero;
using IQOne.Zero.DependencyInjection.Descriptors;

namespace Zero.Sample.Orders.Pricing;

/// <summary>
/// What a product costs right now, according to somewhere else.
/// </summary>
/// <remarks>
/// Stands in for the dependency every real application has and cannot control: a rate
/// service, a tax engine, another team's API. It answers <see cref="ErrorKind.Unavailable"/>
/// when it cannot answer, which is what makes the request worth retrying — and it is why
/// this sample references <c>IQOne.Zero.Resilience</c>.
/// </remarks>
public interface IPricingService : IScoped
{
    /// <summary>What one of this product costs.</summary>
    /// <param name="productCode">Which product.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The price, or why it could not be had.</returns>
    Task<Result<decimal>> PriceAsync(string productCode, CancellationToken cancellationToken);
}

/// <summary>
/// A pricing service that fails the first time it is asked about anything.
/// </summary>
/// <remarks>
/// Deliberately flaky so the sample demonstrates something rather than asserting it: place
/// an order and the first pricing call fails, the retry succeeds, and the request answers
/// 200 with nothing in the handler mentioning retries.
/// </remarks>
public sealed class FlakyPricingService : IPricingService
{
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <inheritdoc />
    public Task<Result<decimal>> PriceAsync(string productCode, CancellationToken cancellationToken)
    {
        bool first;

        lock (_gate) first = _asked.Add(productCode);

        return Task.FromResult(first
            ? Result<decimal>.Failure(
                Error.Unavailable("pricing.unreachable", "The pricing service did not answer."))
            : Result<decimal>.Success(9.99m));
    }
}
