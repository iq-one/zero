namespace IQOne.Zero.BackgroundWork;

/// <summary>
/// Work the application does on a schedule rather than because someone asked.
/// </summary>
/// <remarks>
/// <para>
/// A job is resolved from a <em>fresh scope for every run</em>, so its constructor may take
/// scoped services — a repository, a unit of work, a database context — exactly as a handler
/// does. That is the whole reason the interface exists: the alternative, a hosted service
/// holding one instance forever, captures the first scope's services and keeps them for the
/// life of the process, which Zero already reports as ZERO009 when it can see it and which no
/// analyzer can see once the loop is written by hand.
/// </para>
/// <para>
/// There is no way to register an instance of a job. That is deliberate — the correct thing
/// is meant to be the only thing available.
/// </para>
/// <para>
/// Prefer no class at all. Most background work is "send this command every so often", and
/// <c>AddRecurringCommand</c> expresses that without a type: the command goes through
/// <c>ISender</c>, so validation, authorization, transactions, logging, tracing and metrics
/// all apply to the run without this package knowing any of them exist. Write a job class
/// when the run is genuinely not one command — when it fans out, or reads a list and sends
/// one command per entry.
/// </para>
/// </remarks>
public interface IRecurringJob
{
    /// <summary>
    /// Does the work for one occurrence, then returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns. A body that loops until cancelled runs once and never again — the schedule
    /// cannot bring back a job that has not finished — and nothing reports it, which is the
    /// failure this package exists to remove. There is no <c>while (true)</c> and no
    /// <c>Task.Delay</c> for pacing: state the interval in the schedule instead.
    /// </para>
    /// <para>
    /// Failure is a value. Return <c>Result.Success()</c> or an <see cref="Error"/>; both are
    /// recorded, and neither stops the schedule. An exception is caught, logged with its stack
    /// and counted, and the schedule survives that too.
    /// </para>
    /// </remarks>
    /// <param name="context">Which job this is, which occurrence, and when it was due.</param>
    /// <param name="cancellationToken">
    /// Cancelled when the application is stopping. Pass it to everything awaited: a run that
    /// ignores it holds up shutdown until the host's timeout kills it mid-work. Reported as
    /// ZERO551.
    /// </param>
    /// <returns>Whether the occurrence did what it was meant to.</returns>
    Task<Result> RunAsync(JobRunContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Which occurrence of which job is running, and when it was due.
/// </summary>
/// <remarks>
/// <see cref="ScheduledFor"/> rather than the clock is what a job should reason about. A run
/// that reconciles "everything since last time" and takes its window from the machine clock
/// leaves a gap the size of its own start-up delay, every time; a run that takes it from the
/// occurrence it is serving does not. Reading the clock inside a job is reported as ZERO550.
/// </remarks>
/// <param name="JobName">The job's registered name, which is also its log category suffix.</param>
/// <param name="Number">Which run this is, counted from one, in this process.</param>
/// <param name="ScheduledFor">The occurrence being served.</param>
/// <param name="StartedAt">When the run actually began, which is at or after the occurrence.</param>
public sealed record JobRunContext(
    string JobName,
    long Number,
    DateTimeOffset ScheduledFor,
    DateTimeOffset StartedAt);
