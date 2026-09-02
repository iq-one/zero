using System.Reflection;

namespace IQOne.Zero.ApiSurface.Tests;

/// <summary>
/// Locks the published API surface.
/// </summary>
/// <remarks>
/// Zero promises semantic versioning from its first release. That promise is only worth
/// something if a breaking change is visible to whoever reviews the pull request, so the
/// surface is rendered to text and checked in. Widening it is a one-line diff; narrowing it
/// is a diff nobody can merge by accident.
/// </remarks>
public class ApiSurfaceTests
{
    /// <summary>
    /// Every package that ships an assembly.
    /// </summary>
    /// <remarks>
    /// Derived from the repository rather than listed. A hand-written list covered ten of
    /// the sixteen shipping assemblies, and the six it missed — Persistence, its Entity
    /// Framework provider, Caching, Observability, Authorization and Testing — had no lock
    /// at all. The gap was invisible: the test was green because it never looked. A list
    /// that has to be remembered is a list that will be forgotten.
    /// </remarks>
    public static TheoryData<string> Assemblies
    {
        get
        {
            var source = Path.Combine(Repository, "src");

            var data = new TheoryData<string>();

            foreach (var project in Directory.EnumerateDirectories(source, "IQOne.Zero*").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(project);

                // The metapackage carries no assembly; the generator ships as an analyzer and
                // the tool as an executable. Neither has a surface a consumer compiles against.
                if (name is "IQOne.Zero" or "IQOne.Zero.Generators" or "IQOne.Zero.Tool") continue;
                if (name.EndsWith(".Analyzers", StringComparison.Ordinal)) continue;

                data.Add(name);
            }

            return data;
        }
    }

    private static string Repository
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IQOne.Zero.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("Repository root not found from " + AppContext.BaseDirectory);
        }
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void The_public_surface_matches_what_was_approved(string assemblyName)
    {
        var actual = ApiSurface.Render(Assembly.Load(assemblyName));

        var approvedPath = Path.Combine(AppContext.BaseDirectory, "Approved", $"{assemblyName}.approved.txt");
        var receivedPath = Path.Combine(AppContext.BaseDirectory, "Approved", $"{assemblyName}.received.txt");

        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(receivedPath)!);
            File.WriteAllText(receivedPath, actual);

            Assert.Fail(
                $"No approved surface for {assemblyName}. The current surface was written to " +
                $"{receivedPath}; review it and copy it to Approved/{assemblyName}.approved.txt.");
        }

        var approved = File.ReadAllText(approvedPath).Replace("\r\n", "\n");

        if (approved == actual) return;

        File.WriteAllText(receivedPath, actual);

        Assert.Fail(
            $"The public surface of {assemblyName} changed.\n\n" +
            $"If the change is intended, copy {receivedPath} over Approved/{assemblyName}.approved.txt " +
            "and make sure the version bump matches: a removal or a signature change is breaking.\n\n" +
            Diff(approved, actual));
    }

    /// <summary>A minimal line diff; enough to see what moved without pulling in a library.</summary>
    private static string Diff(string approved, string actual)
    {
        var before = approved.Split('\n');
        var after = actual.Split('\n');

        var removed = before.Except(after, StringComparer.Ordinal).ToList();
        var added = after.Except(before, StringComparer.Ordinal).ToList();

        var lines = removed.Select(l => "- " + l).Concat(added.Select(l => "+ " + l));

        return string.Join('\n', lines.Where(l => l.Length > 2).Take(60));
    }
}
