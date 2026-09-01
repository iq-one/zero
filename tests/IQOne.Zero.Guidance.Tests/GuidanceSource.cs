using System.Text.Json;
using System.Text.RegularExpressions;

namespace IQOne.Zero.Guidance.Tests;

/// <summary>One C# snippet found in the guidance that ships inside a package.</summary>
/// <param name="Origin">Where it came from, as a repository-relative path plus a hint.</param>
/// <param name="Code">The snippet itself.</param>
public sealed record Snippet(string Origin, string Code)
{
    /// <inheritdoc />
    public override string ToString() => Origin;
}

/// <summary>Finds every C# snippet the framework tells its users to write.</summary>
public static partial class GuidanceSource
{
    public static string Repository
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "IQOne.Zero.slnx")))
                    return d.FullName;

            throw new InvalidOperationException("The repository root was not found from the test output folder.");
        }
    }

    /// <summary>The `example` field of every capability manifest.</summary>
    public static IEnumerable<Snippet> ManifestExamples()
    {
        foreach (var manifest in Directory
                     .EnumerateFiles(Path.Combine(Repository, "src"), "capability.json", SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));

            if (!document.RootElement.TryGetProperty("example", out var example)) continue;
            if (example.GetString() is not { Length: > 0 } code) continue;

            yield return new Snippet($"{Relative(manifest)} → example", code);
        }
    }

    /// <summary>
    /// Every fenced C# block in a rule file that ships inside a package.
    /// </summary>
    /// <remarks>
    /// A block fenced <c>```csharp illustrative</c> is skipped. That escape exists for a
    /// snippet that shows a SHAPE rather than code — a "don't" example declaring a second
    /// handler with no body, say. It is deliberately awkward to type and easy to grep,
    /// because the moment it becomes convenient it will be used to silence a real defect.
    /// </remarks>
    public static IEnumerable<Snippet> RuleSnippets()
    {
        foreach (var rule in Directory
                     .EnumerateFiles(Path.Combine(Repository, "src"), "*.md", SearchOption.AllDirectories)
                     .Where(p => p.Contains($"{Path.DirectorySeparatorChar}rules{Path.DirectorySeparatorChar}"))
                     .Order(StringComparer.Ordinal))
        {
            var index = 0;

            foreach (Match match in Fence().Matches(File.ReadAllText(rule)))
                yield return new Snippet($"{Relative(rule)} → block {++index}", match.Groups["code"].Value);
        }
    }

    private static string Relative(string path) => Path.GetRelativePath(Repository, path);

    [GeneratedRegex(@"```csharp[ \t]*\r?\n(?<code>.*?)```", RegexOptions.Singleline)]
    private static partial Regex Fence();
}
