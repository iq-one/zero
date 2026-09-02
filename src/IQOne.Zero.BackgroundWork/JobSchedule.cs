namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// How often a job runs.
/// </summary>
/// <remarks>
/// <para>
/// An interval, and nothing else. There is no cron expression here, and that is a decision
/// rather than an omission: a parser is a dependency, a second syntax to learn and a second
/// place for a schedule to be wrong, and the two things people actually reach for cron to
/// express — "every weekday at 06:00" and "the first working day of the month" — are calendar
/// questions no expression can answer correctly. Holidays, month ends and the day the clocks
/// change belong to the application's domain, not to a five-field string.
/// </para>
/// <para>
/// So for "every weekday at 06:00" there are two honest answers. Either run the job often —
/// <c>Every(TimeSpan.FromMinutes(1))</c> — and let its body ask its own calendar whether now
/// is the moment, which is the only version that knows what a working day is where you are;
/// or take the schedule out of the process entirely and let the platform's scheduled job
/// (a Kubernetes <c>CronJob</c>, an Azure Container Apps job, a queue message with a delay)
/// start the same command through the same <c>ISender</c>. The second answer also happens to
/// solve running once across three replicas, which this package does not.
/// </para>
/// <para>
/// A <see langword="default"/> instance has no period and is refused at registration. Build
/// one with <see cref="Every(TimeSpan)"/>.
/// </para>
/// </remarks>
public readonly record struct JobSchedule
{
    private JobSchedule(TimeSpan period, TimeSpan initialDelay)
    {
        Period = period;
        InitialDelay = initialDelay;
    }

    /// <summary>How long between one occurrence and the next.</summary>
    /// <remarks>
    /// Measured from occurrence to occurrence, not from the end of one run to the start of the
    /// next, so a job "every five minutes" stays on the five-minute marks it started on
    /// however long an individual run takes.
    /// </remarks>
    public TimeSpan Period { get; }

    /// <summary>How long after startup the first occurrence falls.</summary>
    /// <remarks>
    /// One full period by default, so nothing fires while the application is still coming up —
    /// which is when it is slowest and when a rolling deployment has every replica starting at
    /// once. Pass <see cref="TimeSpan.Zero"/> to run immediately at startup, deliberately.
    /// </remarks>
    public TimeSpan InitialDelay { get; }

    /// <summary>A job that runs every <paramref name="period"/>, starting one period from now.</summary>
    /// <param name="period">How long between occurrences. Must be greater than zero.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="period"/> is not positive.</exception>
    public static JobSchedule Every(TimeSpan period) => Every(period, period);

    /// <summary>A job that runs every <paramref name="period"/>, starting after a delay you choose.</summary>
    /// <param name="period">How long between occurrences. Must be greater than zero.</param>
    /// <param name="initialDelay">How long after startup the first occurrence falls. May be zero.</param>
    /// <returns>The schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="period"/> is not positive, or <paramref name="initialDelay"/> is negative.
    /// </exception>
    public static JobSchedule Every(TimeSpan period, TimeSpan initialDelay)
    {
        if (period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(period), period, "A recurring job needs a period greater than zero.");

        if (initialDelay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(initialDelay), initialDelay, "A first occurrence cannot fall before startup.");

        return new JobSchedule(period, initialDelay);
    }

    /// <summary>
    /// The occurrence after <paramref name="ran"/>, and how many fell while the run was still going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Occurrences that passed during a run are <em>dropped</em>, not queued. A job that takes
    /// ninety seconds on a sixty-second period runs, misses one mark, and then runs again on
    /// the next one. Queueing them would mean a job recovering from a stall immediately runs
    /// the backlog it accumulated while stalled — which is exactly the moment the dependency
    /// it stalled on can least afford it.
    /// </para>
    /// <para>
    /// Arithmetic rather than a loop, so a process resumed after a long suspension does not
    /// spend that time counting occurrences it is about to discard anyway.
    /// </para>
    /// </remarks>
    /// <param name="ran">The occurrence that has just been served.</param>
    /// <param name="now">The current time.</param>
    /// <returns>The next occurrence, and the number skipped to reach it.</returns>
    internal (DateTimeOffset Due, long Skipped) Next(DateTimeOffset ran, DateTimeOffset now)
    {
        var due = ran + Period;

        if (due >= now) return (due, 0);

        var behind = (now - due).Ticks;

        // Ceiling division: an occurrence landing exactly on `now` is served, not skipped.
        var skipped = (behind + Period.Ticks - 1) / Period.Ticks;

        return (due + new TimeSpan(skipped * Period.Ticks), skipped);
    }

    /// <inheritdoc />
    public override string ToString()
        => InitialDelay == Period
            ? $"every {Period}"
            : $"every {Period}, first after {InitialDelay}";
}
