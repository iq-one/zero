namespace IQOne.Zero.BackgroundWork;

/// <summary>What runs, and whether anything runs at all.</summary>
/// <remarks>
/// <para>
/// Deliberately small. Everything about one job — how often, what it does, what it is called —
/// belongs to that job's registration. What is left here are the two decisions that belong to
/// a deployment rather than to a job.
/// </para>
/// <para>
/// One instance for the application, held as a singleton and read as each occurrence falls
/// due, so a second <c>AddZeroBackgroundWork</c> call refines this instance rather than
/// replacing it: a module and a host may both configure background work and neither silently
/// undoes the other. Reading it per occurrence rather than at startup is what lets
/// <see cref="Disabled"/> be changed by a reloaded configuration and take effect on the next
/// tick instead of on the next deployment.
/// </para>
/// </remarks>
public sealed class BackgroundWorkOptions
{
    /// <summary>
    /// Whether any job runs. On by default.
    /// </summary>
    /// <remarks>
    /// The switch a test reaches for. A test that starts the real host gets the real schedule
    /// with it, and a suite where a reconciliation fires halfway through an unrelated test is a
    /// suite that fails for reasons nobody can reproduce. Turning this off leaves every
    /// registration in place — the jobs are still listed by <see cref="IBackgroundWorkStatus"/>
    /// and can still be run by hand — and starts none of them.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Jobs that stay registered but do not run, by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the job that has to be stopped now: it is hammering a dependency, or it is the one
    /// job that must not run in this environment. Naming it here is a configuration change
    /// rather than a deployment, and it leaves the job visible in the status report as
    /// switched off rather than making it vanish.
    /// </para>
    /// <para>
    /// It also answers "three replicas, one nightly job" the crude way: enable the job on one
    /// replica's configuration and disable it on the others. That is not coordination — see
    /// the package's rule file — but it is honest, and for a fixed deployment it is enough.
    /// </para>
    /// </remarks>
    public ISet<string> Disabled { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a named job is allowed to run right now.</summary>
    /// <param name="name">The job's registered name.</param>
    /// <returns><see langword="true"/> when it may run.</returns>
    internal bool Runs(string name) => Enabled && !Disabled.Contains(name);
}
