namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// One registered job: what it is called, when it runs, and what running it means.
/// </summary>
/// <remarks>
/// The body is a delegate over a scope's <see cref="IServiceProvider"/> rather than an
/// instance, and that is the point. A descriptor holding a job object would hold that object's
/// dependencies with it — the captive-dependency failure the whole capability exists to
/// remove — so what is stored is a way to obtain the job once the run has a scope of its own.
/// </remarks>
/// <param name="Name">The job's registered name, unique in the application.</param>
/// <param name="Schedule">How often it runs.</param>
/// <param name="Run">Resolves the work from the run's scope and performs it.</param>
internal sealed record RecurringJobDescriptor(
    string Name,
    JobSchedule Schedule,
    Func<IServiceProvider, JobRunContext, CancellationToken, Task<Result>> Run);

/// <summary>
/// What is registered, and how each of it is going.
/// </summary>
/// <remarks>
/// <para>
/// One instance for the application, created by <c>AddZeroBackgroundWork</c> and registered as
/// an instance so that a later <c>AddRecurringJob</c> call can find it in the collection
/// before any provider exists. The same shape <c>AddZeroMessaging</c> uses for its dispatch
/// table, and for the same reason: a capability's entry point has to work whether or not the
/// application has modules.
/// </para>
/// <para>
/// The counters are written by the scheduler's loops and read by whoever holds
/// <see cref="IBackgroundWorkStatus"/> — a health check, most likely, on a request thread. So
/// every read takes a snapshot under the same lock the writes take; a status report that
/// tears is a status report somebody will chase.
/// </para>
/// </remarks>
internal sealed class RecurringJobCatalog : IBackgroundWorkStatus
{
    private readonly object _gate = new();
    private readonly List<RecurringJobDescriptor> _jobs = [];
    private readonly Dictionary<string, JobState> _state = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Called once a run has finished and everything about it has been written down.
    /// </summary>
    /// <remarks>
    /// The seam a test uses to wait for a run instead of sleeping for one. It is internal and
    /// stays internal: an application that wants to react to a run reads
    /// <see cref="IBackgroundWorkStatus"/>, subscribes to the meter, or reads the log — three
    /// mechanisms already, and a fourth public one would be a fourth thing to keep working.
    /// Raised after the log line and the measurement, so a test that awaits it can assert on
    /// both.
    /// </remarks>
    internal Action<JobRun>? Completed { get; set; }

    /// <summary>Every registered job, in registration order.</summary>
    internal IReadOnlyList<RecurringJobDescriptor> Registered
    {
        get { lock (_gate) return [.. _jobs]; }
    }

    /// <inheritdoc />
    public IReadOnlyList<RecurringJobStatus> Jobs
    {
        get { lock (_gate) return [.. _jobs.Select(j => _state[j.Name].Snapshot(j))]; }
    }

    /// <inheritdoc />
    public RecurringJobStatus? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => string.Equals(j.Name, name, StringComparison.OrdinalIgnoreCase));

            return job is null ? null : _state[job.Name].Snapshot(job);
        }
    }

    /// <summary>
    /// Adds a job, refusing a name that is already taken.
    /// </summary>
    /// <remarks>
    /// Names are the handle for everything else — the log category, the metric tag, the
    /// <c>Disabled</c> list, the status report — so two jobs sharing one would make all four
    /// ambiguous at once, and only the last-registered one would be findable. Refusing at
    /// registration puts the failure in the startup path, next to the two call sites that
    /// caused it.
    /// </remarks>
    /// <param name="job">The job to register.</param>
    /// <exception cref="InvalidOperationException">A job is already registered under that name.</exception>
    internal void Add(RecurringJobDescriptor job)
    {
        lock (_gate)
        {
            if (_state.ContainsKey(job.Name))
                throw new InvalidOperationException(
                    $"A recurring job named '{job.Name}' is already registered. Job names identify a job in " +
                    "the log, in metrics and in BackgroundWorkOptions.Disabled, so they have to be unique. " +
                    "Pass a name to AddRecurringJob or AddRecurringCommand to tell the two apart.");

            _jobs.Add(job);
            _state[job.Name] = new JobState();
        }
    }

    /// <summary>Records when a job's next occurrence falls.</summary>
    /// <param name="name">The job's registered name.</param>
    /// <param name="dueAt">When the occurrence falls, or <see langword="null"/> when nothing is scheduled.</param>
    internal void Due(string name, DateTimeOffset? dueAt)
    {
        lock (_gate) _state[name].NextDueAt = dueAt;
    }

    /// <summary>Records occurrences dropped because a run was still going.</summary>
    /// <param name="name">The job's registered name.</param>
    /// <param name="count">How many were dropped.</param>
    internal void Dropped(string name, long count)
    {
        lock (_gate) _state[name].Skipped += count;
    }

    /// <summary>Records a finished run.</summary>
    /// <param name="run">What the run did.</param>
    /// <returns>How many runs have now failed in a row.</returns>
    internal long Record(JobRun run)
    {
        lock (_gate)
        {
            var state = _state[run.Name];

            state.Runs++;
            state.LastStartedAt = run.StartedAt;
            state.LastOutcome = run.Outcome;
            state.LastFailure = run.Failure;

            // A cancelled run is the application stopping, not the job being wrong, so it
            // neither counts as a failure nor clears a streak of them: a deployment must not
            // reset the number a health check is watching.
            switch (run.Outcome)
            {
                case JobRunOutcome.Succeeded:
                    state.ConsecutiveFailures = 0;

                    break;

                case JobRunOutcome.Failed:
                case JobRunOutcome.Faulted:
                    state.Failures++;
                    state.ConsecutiveFailures++;

                    break;
            }

            return state.ConsecutiveFailures;
        }
    }

    /// <summary>How many runs a job has started, so the next one can be numbered.</summary>
    /// <param name="name">The job's registered name.</param>
    /// <returns>The number to give the next run, counted from one.</returns>
    internal long NextNumber(string name)
    {
        lock (_gate) return ++_state[name].Started;
    }

    /// <summary>The mutable half, kept apart from the immutable descriptor.</summary>
    private sealed class JobState
    {
        internal long Started { get; set; }

        internal long Runs { get; set; }

        internal long Failures { get; set; }

        internal long ConsecutiveFailures { get; set; }

        internal long Skipped { get; set; }

        internal DateTimeOffset? NextDueAt { get; set; }

        internal DateTimeOffset? LastStartedAt { get; set; }

        internal JobRunOutcome? LastOutcome { get; set; }

        internal string? LastFailure { get; set; }

        /// <summary>
        /// Whether the job is currently allowed to run.
        /// </summary>
        /// <remarks>
        /// Written by the loop rather than read from the options here, so the status report
        /// says what the scheduler is actually doing rather than what the options would imply
        /// if the host had started.
        /// </remarks>
        internal bool Enabled { get; set; } = true;

        internal RecurringJobStatus Snapshot(RecurringJobDescriptor job) => new(
            job.Name,
            job.Schedule.Period,
            Enabled,
            NextDueAt,
            LastStartedAt,
            LastOutcome,
            LastFailure,
            Runs,
            Failures,
            ConsecutiveFailures,
            Skipped);
    }

    /// <summary>Records whether a job is allowed to run.</summary>
    /// <param name="name">The job's registered name.</param>
    /// <param name="enabled">Whether it may run.</param>
    internal void Allowed(string name, bool enabled)
    {
        lock (_gate) _state[name].Enabled = enabled;
    }
}
