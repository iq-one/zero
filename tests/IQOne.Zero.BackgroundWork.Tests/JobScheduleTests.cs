using IQOne.Zero.BackgroundWork;

namespace IQOne.Zero.BackgroundWork.Tests;

/// <summary>
/// When the next occurrence is due, and how many were missed getting there.
/// </summary>
/// <remarks>
/// This is the arithmetic behind "a run never overlaps the previous one". A job whose body
/// outlasts its interval has to land somewhere, and landing on a queue of every occurrence
/// it slept through would turn one slow night into a thundering herd at breakfast.
/// </remarks>
public class JobScheduleTests
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static JobSchedule EveryMinute => JobSchedule.Every(TimeSpan.FromMinutes(1));

    [Fact]
    public void A_run_that_finished_in_time_gets_the_next_occurrence_and_skips_none()
    {
        var (due, skipped) = EveryMinute.Next(Noon, Noon.AddSeconds(10));

        due.Should().Be(Noon.AddMinutes(1));
        skipped.Should().Be(0);
    }

    [Fact]
    public void A_run_that_outlasted_its_interval_skips_the_occurrences_it_slept_through()
    {
        // Served noon, finished at 12:03:30. Noon+1, +2 and +3 are gone.
        var (due, skipped) = EveryMinute.Next(Noon, Noon.AddMinutes(3).AddSeconds(30));

        due.Should().Be(Noon.AddMinutes(4));
        skipped.Should().Be(3, "a slow night must not become a queue of runs at breakfast");
    }

    [Fact]
    public void An_occurrence_landing_exactly_on_now_is_served_rather_than_skipped()
    {
        var (due, skipped) = EveryMinute.Next(Noon, Noon.AddMinutes(1));

        due.Should().Be(Noon.AddMinutes(1));
        skipped.Should().Be(0, "it is due now, not overdue");
    }

    [Fact]
    public void The_next_occurrence_is_always_in_the_future()
    {
        var now = Noon.AddHours(9).AddSeconds(17);

        var (due, _) = EveryMinute.Next(Noon, now);

        due.Should().BeAfter(now,
            "a due time already in the past would run immediately and stay behind forever");
    }

    [Fact]
    public void An_initial_delay_is_separate_from_the_period()
    {
        var schedule = JobSchedule.Every(TimeSpan.FromHours(1), TimeSpan.FromMinutes(2));

        schedule.InitialDelay.Should().Be(TimeSpan.FromMinutes(2),
            "a job that must not fire during start-up needs the first wait to differ from the rest");
        schedule.Period.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void A_period_that_is_not_positive_is_refused()
    {
        var zero = () => JobSchedule.Every(TimeSpan.Zero);

        zero.Should().Throw<ArgumentOutOfRangeException>(
            "a schedule with no period is a loop with no pacing, which is what this replaces");
    }
}
