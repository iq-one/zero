using IQOne.Zero.BackgroundWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace IQOne.Zero.BackgroundWork.Tests;

/// <summary>
/// The host, driven by a clock the test controls.
/// </summary>
/// <remarks>
/// No test here sleeps. A schedule tested by waiting is slow, and eventually flaky on a
/// loaded machine — which is most of why the package takes a <see cref="TimeProvider"/> at
/// all rather than calling <c>Task.Delay</c> directly.
/// </remarks>
public class RecurringJobHostTests
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(IHostedService Host, FakeTimeProvider Time, ServiceProvider Provider)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private static Harness Build(Action<IServiceCollection> configure)
    {
        var time = new FakeTimeProvider(Noon);
        var services = new ServiceCollection();

        services.AddSingleton<TimeProvider>(time);
        configure(services);

        var provider = services.BuildServiceProvider();

        return new Harness(provider.GetRequiredService<IHostedService>(), time, provider);
    }

    /// <summary>
    /// Moves the clock forward and waits for the run it made due.
    /// </summary>
    /// <remarks>
    /// The fake clock decides <em>when</em> a run is due; it cannot decide when the loop's
    /// continuation is scheduled, which is the thread pool's business. So the test signals
    /// from inside the job and waits for that. The timeout is real, but only as a backstop:
    /// on a working loop nothing waits, and on a broken one the test fails instead of hanging.
    /// </remarks>
    private static async Task Advance(FakeTimeProvider time, TimeSpan by, SemaphoreSlim ran, int expected = 1)
    {
        time.Advance(by);

        for (var i = 0; i < expected; i++)
            (await ran.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue(
                "the run this advance made due never happened");
    }

    /// <summary>Never signalled: for asserting that nothing runs.</summary>
    private static async Task AdvanceAndExpectNothing(FakeTimeProvider time, TimeSpan by)
    {
        time.Advance(by);

        // Long enough that a loop which was going to run would have; short enough not to matter.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public async Task A_job_runs_when_its_occurrence_falls_due()
    {
        var runs = 0;
        var ran = new SemaphoreSlim(0);

        await using var harness = Build(services =>
        {
            services.AddZeroBackgroundWork();
            services.AddRecurringJob("counter", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (_, _, _) =>
                {
                    Interlocked.Increment(ref runs);
                    ran.Release();

                    return Task.FromResult(Result.Success());
                });
        });

        await harness.Host.StartAsync(CancellationToken.None);

        runs.Should().Be(0, "the first occurrence is one interval away, not immediate");

        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);

        runs.Should().Be(1);
    }

    [Fact]
    public async Task Each_run_gets_its_own_scope()
    {
        var seen = new List<Guid>();
        var ran = new SemaphoreSlim(0);

        await using var harness = Build(services =>
        {
            services.AddScoped<ScopeMarker>();
            services.AddZeroBackgroundWork();
            services.AddRecurringJob("scoped", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (provider, _, _) =>
                {
                    lock (seen) seen.Add(provider.GetRequiredService<ScopeMarker>().Id);
                    ran.Release();

                    return Task.FromResult(Result.Success());
                });
        });

        await harness.Host.StartAsync(CancellationToken.None);

        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);
        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);

        seen.Should().HaveCountGreaterThanOrEqualTo(2);
        seen.Distinct().Should().HaveCount(seen.Count,
            "a job holding one scope across runs holds one DbContext across runs, which works " +
            "until the connection is dropped and then fails everywhere at once");
    }

    [Fact]
    public async Task A_failing_run_does_not_stop_the_schedule()
    {
        var runs = 0;
        var ran = new SemaphoreSlim(0);

        await using var harness = Build(services =>
        {
            services.AddZeroBackgroundWork();
            services.AddRecurringJob("failing", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (_, _, _) =>
                {
                    Interlocked.Increment(ref runs);
                    ran.Release();

                    return Task.FromResult(Result.Failure(Error.Failure("job.no", "Refused.")));
                });
        });

        await harness.Host.StartAsync(CancellationToken.None);

        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);
        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);

        runs.Should().BeGreaterThanOrEqualTo(2, "run 400 of 10,000 failing must not stop the other 9,600");
    }

    [Fact]
    public async Task A_throwing_run_does_not_stop_the_schedule_either()
    {
        var runs = 0;
        var ran = new SemaphoreSlim(0);

        await using var harness = Build(services =>
        {
            services.AddZeroBackgroundWork();
            services.AddRecurringJob("throwing", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (_, _, _) =>
                {
                    Interlocked.Increment(ref runs);
                    ran.Release();

                    throw new InvalidOperationException("a defect in a job");
                });
        });

        await harness.Host.StartAsync(CancellationToken.None);

        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);
        await Advance(harness.Time, TimeSpan.FromMinutes(1), ran);

        runs.Should().BeGreaterThanOrEqualTo(2);

        harness.Provider.GetRequiredService<IBackgroundWorkStatus>()
            .Find("throwing")!.Failures.Should().BeGreaterThan(0,
                "an exception that stops nothing must still be counted, or it is invisible");
    }

    [Fact]
    public async Task A_disabled_job_stays_registered_and_does_not_run()
    {
        var runs = 0;

        await using var harness = Build(services =>
        {
            services.AddZeroBackgroundWork(options => options.Disabled.Add("quiet"));
            services.AddRecurringJob("quiet", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (_, _, _) => { Interlocked.Increment(ref runs); return Task.FromResult(Result.Success()); });
        });

        await harness.Host.StartAsync(CancellationToken.None);
        await AdvanceAndExpectNothing(harness.Time, TimeSpan.FromMinutes(5));

        runs.Should().Be(0);

        harness.Provider.GetRequiredService<IBackgroundWorkStatus>()
            .Find("quiet").Should().NotBeNull("switched off is not the same as absent");
    }

    [Fact]
    public async Task Switching_the_whole_capability_off_runs_nothing()
    {
        var runs = 0;

        await using var harness = Build(services =>
        {
            services.AddZeroBackgroundWork(options => options.Enabled = false);
            services.AddRecurringJob("counter", JobSchedule.Every(TimeSpan.FromMinutes(1)),
                (_, _, _) => { Interlocked.Increment(ref runs); return Task.FromResult(Result.Success()); });
        });

        await harness.Host.StartAsync(CancellationToken.None);
        await AdvanceAndExpectNothing(harness.Time, TimeSpan.FromMinutes(5));

        runs.Should().Be(0, "this is the switch a test reaches for, so it has to actually stop everything");
    }

    private sealed class ScopeMarker
    {
        public Guid Id { get; } = Guid.NewGuid();
    }
}
