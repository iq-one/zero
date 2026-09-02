using System.ComponentModel.DataAnnotations;

namespace Zero.Sample.Orders.Configuration;

/// <summary>
/// How ordering behaves in this deployment.
/// </summary>
/// <remarks>
/// A validated options type rather than <c>IConfiguration["Ordering:Hold"]</c> at the point
/// of use. A missing or nonsensical value stops the application at startup with a message
/// naming it, instead of surfacing on the first order somebody placed.
/// </remarks>
public sealed class OrderingOptions
{
    /// <summary>How long an order waits for payment before its stock goes back.</summary>
    [Range(typeof(TimeSpan), "00:01:00", "7.00:00:00")]
    public TimeSpan PaymentWindow { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How many expired orders one sweep releases.</summary>
    /// <remarks>
    /// Bounded so a backlog is worked through in several transactions rather than one that
    /// holds locks for minutes.
    /// </remarks>
    [Range(1, 1000)]
    public int ExpirySweepSize { get; set; } = 100;

    /// <summary>Most lines one order may carry.</summary>
    [Range(1, 200)]
    public int MaxLines { get; set; } = 50;
}
