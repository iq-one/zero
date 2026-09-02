namespace IQOne.Zero.Resilience;

/// <summary>How long to wait before the next attempt.</summary>
/// <remarks>
/// Separated from the behaviour because it is the part with arithmetic in it, and arithmetic
/// is worth testing on its own: the interesting cases here are the ones a pipeline test
/// would have to construct an outage to reach.
/// </remarks>
internal static class Backoff
{
    /// <summary>The wait after a given attempt has failed.</summary>
    /// <param name="options">The first delay, the factor, the ceiling and the jitter.</param>
    /// <param name="attempt">Which attempt just failed, counting from one.</param>
    /// <returns>How long to wait before the next one.</returns>
    public static TimeSpan Delay(ResilienceOptions options, int attempt)
    {
        // In floating point the whole way, so a large factor or a high attempt number
        // saturates at infinity instead of overflowing a long and coming back as a negative
        // TimeSpan — which is a wait of zero, i.e. exactly the hammering this exists to stop.
        var grown = options.FirstDelay.Ticks * Math.Pow(options.BackoffFactor, attempt - 1);
        var capped = Math.Min(grown, options.MaxDelay.Ticks);

        // Jitter is subtracted from the top rather than added to it: the cap has to stay a
        // cap, or the longest possible wait would be MaxDelay plus the random part.
        var fixedPart = capped * (1 - options.Jitter);
        var randomPart = capped * options.Jitter * Random.Shared.NextDouble();

        return TimeSpan.FromTicks((long)(fixedPart + randomPart));
    }
}
