namespace IQOne.Zero.Caching;

/// <summary>How caching behaves, for the whole application.</summary>
/// <remarks>
/// Everything about a single answer — whether it is cached at all, under what key, for how
/// long — belongs to the query. What is left here is the handful of decisions that are the
/// same for every query in a deployment.
/// </remarks>
public sealed class CachingOptions
{
    /// <summary>
    /// Whether anything is cached at all. On by default.
    /// </summary>
    /// <remarks>
    /// One switch, so a test can turn caching off without unpicking its registrations. A test
    /// that passes alone and fails in a suite because the previous test left an answer behind
    /// is a test nobody trusts, and the usual reaction is to stop trusting the suite.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// How long an answer lives when the query does not say. Five minutes by default.
    /// </summary>
    /// <remarks>
    /// Short on purpose. A default that is generous is a default nobody overrides, and the
    /// first sign of one that is too generous is a caller acting on data that changed an hour
    /// ago.
    /// </remarks>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Put in front of every key Zero writes. <c>zero:</c> by default.
    /// </summary>
    /// <remarks>
    /// The store may be shared — with the application's own entries, and with other services
    /// pointed at the same server. Give each one its own prefix and an accidental collision
    /// becomes impossible rather than unlikely. Changing it also retires every entry written
    /// under the old one, which is the cheapest way to invalidate across a deployment whose
    /// answers changed shape.
    /// </remarks>
    public string KeyPrefix { get; set; } = "zero:";
}
