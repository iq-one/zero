namespace Zero.Sample.Orders.Pricing;

/// <summary>
/// The pricing module.
/// </summary>
/// <remarks>
/// Nothing is written here, and that is the point worth noticing.
/// <c>FlakyPricingService</c> implements <c>IPricingService</c>, which carries
/// <c>IScoped</c> — so the generator knows both the service type and the lifetime and
/// registers it. An <c>AddScoped&lt;IPricingService, FlakyPricingService&gt;()</c> would be
/// a second statement of something already stated, and the two could drift.
/// </remarks>
public sealed partial class Module;
