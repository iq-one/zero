using System.Text.Json;

namespace IQOne.Zero.Tool.Rules;

/// <summary>
/// Finds the Zero packages a project restored and reads the rule files inside them.
/// </summary>
/// <remarks>
/// The restore assets file is the authority on which versions are actually in use, so the
/// rules written out always match the packages the project builds against. Reading a
/// checked-in list instead would drift the moment someone upgraded.
/// </remarks>
internal static class RuleReader
{
    private const string PackagePrefix = "IQOne.Zero";

    public static IReadOnlyList<RuleFile> Read(string assetsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = document.RootElement;

        var folders = root.TryGetProperty("packageFolders", out var packageFolders)
            ? packageFolders.EnumerateObject().Select(f => f.Name).ToList()
            : [];

        if (folders.Count == 0)
            throw new ZeroToolException(
                $"'{assetsFile}' names no package folder. Run 'dotnet restore' and try again.");

        var rules = new List<RuleFile>();

        foreach (var (id, version) in ZeroPackages(root))
        {
            var directory = folders
                .Select(folder => Path.Combine(folder, id.ToLowerInvariant(), version, "zero", "rules"))
                .FirstOrDefault(Directory.Exists);

            if (directory is null) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).Order())
                rules.Add(Parse(id, version, File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
        }

        return [.. rules.OrderBy(r => r.Package, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    private static IEnumerable<(string Id, string Version)> ZeroPackages(JsonElement root)
    {
        if (!root.TryGetProperty("libraries", out var libraries)) yield break;

        foreach (var library in libraries.EnumerateObject())
        {
            // Keys are "Package.Id/1.2.3".
            var separator = library.Name.LastIndexOf('/');
            if (separator < 0) continue;

            var id = library.Name[..separator];

            if (!id.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            yield return (id, library.Name[(separator + 1)..]);
        }
    }

    /// <summary>Reads the YAML frontmatter a rule file opens with, then returns the body.</summary>
    private static RuleFile Parse(string package, string version, string text, string fallbackId)
    {
        var id = fallbackId;
        var title = fallbackId;
        var enforcedBy = new List<string>();
        var body = text;

        if (text.StartsWith("---", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---", 3, StringComparison.Ordinal);

            if (end > 0)
            {
                foreach (var line in text[3..end].Split('\n'))
                {
                    var colon = line.IndexOf(':');
                    if (colon < 0) continue;

                    var key = line[..colon].Trim();
                    var value = line[(colon + 1)..].Trim();

                    switch (key)
                    {
                        case "id": id = value; break;
                        case "title": title = value; break;
                        case "enforced-by":
                            enforcedBy.AddRange(value.Trim('[', ']')
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            break;
                    }
                }

                body = text[(end + 4)..].TrimStart('\n');
            }
        }

        return new RuleFile(package, version, id, title, enforcedBy, body.TrimEnd());
    }
}

/// <summary>A failure the user can act on. Reported without a stack trace.</summary>
internal sealed class ZeroToolException(string message) : Exception(message);
