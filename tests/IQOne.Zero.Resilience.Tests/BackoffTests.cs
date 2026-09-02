using IQOne.Zero.Resilience;

namespace IQOne.Zero.Resilience.Tests;

/// <summary>
/// How long to wait before the next attempt.
/// </summary>
/// <remarks>
/// Arithmetic, so it is tested as arithmetic. The failure this guards against is a wait that
/// rounds to zero — which is not "retry quickly", it is exactly the hammering the package
/// exists to prevent, and it only shows up under the load that caused the retry.
/// </remarks>
public class BackoffTests
{
    private static ResilienceOptions Fixed => new()
    {
        FirstDelay = TimeSpan.FromMilliseconds(200),
        BackoffFactor = 2.0,
        MaxDelay = TimeSpan.FromSeconds(5),
        Jitter = 0
    };

    [Fact]
    public void The_wait_grows_with_each_attempt()
    {
        var options = Fixed;

        Backoff.Delay(options, 1).Should().Be(TimeSpan.FromMilliseconds(200));
        Backoff.Delay(options, 2).Should().Be(TimeSpan.FromMilliseconds(400));
        Backoff.Delay(options, 3).Should().Be(TimeSpan.FromMilliseconds(800));
    }

    [Fact]
    public void The_wait_stops_growing_at_the_cap()
    {
        var options = Fixed;

        Backoff.Delay(options, 20).Should().Be(TimeSpan.FromSeconds(5),
            "an uncapped doubling reaches hours, and a caller waiting hours has hung");
    }

    [Fact]
    public void Jitter_spreads_the_wait_without_ever_reaching_zero()
    {
        var options = Fixed;
        options.Jitter = 0.5;

        var waits = Enumerable.Range(0, 200).Select(_ => Backoff.Delay(options, 1)).ToList();

        waits.Should().OnlyContain(w => w > TimeSpan.Zero,
            "a wait that rounds to zero is the hammering this exists to stop");

        waits.Distinct().Should().HaveCountGreaterThan(1,
            "without spread, every caller that failed together retries together and arrives together");
    }

    [Fact]
    public void No_jitter_makes_the_wait_repeatable()
    {
        var options = Fixed;

        Backoff.Delay(options, 2).Should().Be(Backoff.Delay(options, 2));
    }
}
