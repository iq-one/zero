using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Internal;

/// <summary>
/// Keeps a bug in a generator from being a bug in everybody's build.
/// </summary>
/// <remarks>
/// <para>
/// A generator that throws fails the compilation with CS8785 and produces nothing — and
/// generated code CANNOT BE EDITED: it is written during compilation and there is no file the
/// compiler reads back. So a framework bug leaves the person whose build stopped with nothing
/// to change and nothing to try but a new framework release.
/// </para>
/// <para>
/// Wrapping every generator's work turns that into a diagnostic naming what failed. It does
/// not make the build succeed — the output really is missing — but it says which declaration
/// caused it, which is the difference between "wait for a release" and "take that one over by
/// hand". Every generator here leaves a way to do that: the attribute-driven ones stand down
/// when their attribute is removed, and a map or a module written by hand wins outright.
/// </para>
/// </remarks>
internal static class Guard
{
    /// <summary>Runs generator work, reporting a throw instead of failing the compilation.</summary>
    /// <param name="context">Where to report.</param>
    /// <param name="descriptor">The diagnostic for this generator's failures.</param>
    /// <param name="subject">What was being worked on, for the message.</param>
    /// <param name="location">Where to point.</param>
    /// <param name="work">The work.</param>
    public static void Run(
        SourceProductionContext context,
        DiagnosticDescriptor descriptor,
        string subject,
        Location? location,
        Action work)
    {
        try
        {
            work();
        }
        catch (Exception exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, subject, Describe(exception)));
        }
    }

    /// <summary>The exception on one line, because a diagnostic message is one line.</summary>
    public static string Describe(Exception exception)
        => $"{exception.GetType().Name}: {exception.Message.Replace("\r", " ").Replace("\n", " ")}";
}
