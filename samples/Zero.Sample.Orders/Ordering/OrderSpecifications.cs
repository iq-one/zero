using IQOne.Zero.Persistence;
using Zero.Sample.Orders.Data;

namespace Zero.Sample.Orders.Ordering;

/// <summary>One order by the reference the caller chose.</summary>
public sealed class OrderByReference : Specification<Order>
{
    /// <summary>Builds the query.</summary>
    /// <param name="reference">The reference to look for.</param>
    public OrderByReference(string reference) => Where(order => order.Reference == reference);
}

/// <summary>One order, with its lines, ready to be read.</summary>
public sealed class OrderForReading : Specification<Order>
{
    /// <summary>Builds the query.</summary>
    /// <param name="id">Which order.</param>
    public OrderForReading(int id)
    {
        Where(order => order.Id == id);
        ReadOnly();
    }
}

/// <summary>
/// Orders that have waited for payment too long.
/// </summary>
/// <remarks>
/// A named class, so "overdue" is defined once. Two handlers cannot disagree about what it
/// means, and the definition can be checked against a list in memory with no database —
/// which is how the sample's tests check it.
/// </remarks>
public sealed class UnpaidOrdersDueBefore : Specification<Order>
{
    /// <summary>Builds the query.</summary>
    /// <param name="asOf">
    /// The moment to compare against. Passed in rather than read from a clock, so a job
    /// serves the occurrence it was scheduled for.
    /// </param>
    /// <param name="take">How many to take in one sweep.</param>
    public UnpaidOrdersDueBefore(DateTimeOffset asOf, int take)
    {
        Where(order => order.State == OrderState.AwaitingPayment);
        Where(order => order.ExpiresAt < asOf);
        OrderBy(order => order.ExpiresAt);
        Page(0, take);
    }
}

/// <summary>
/// Every order for a customer, deleted ones included.
/// </summary>
/// <remarks>
/// Opts out of exactly one filter. A support view that has to explain what happened to an
/// order needs the deleted rows; it does not need to stop filtering by anything else, and
/// naming the filter is what keeps those two apart.
/// </remarks>
public sealed class OrderHistory : Specification<Order>
{
    /// <summary>Builds the query.</summary>
    /// <param name="customerId">Whose history.</param>
    public OrderHistory(string customerId)
    {
        Where(order => order.CustomerId == customerId);
        OrderByDescending(order => order.CreatedAt);
        IgnoreFilter(SoftDeleteConvention.Name);
        ReadOnly();
    }
}
