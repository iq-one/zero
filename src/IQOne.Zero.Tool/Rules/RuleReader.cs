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
    public static IReadOnlyList<RuleFile> Read(IEnumerable<InstalledPackage> packages)
    {
        var rules = new List<RuleFile>();

        foreach (var package in packages)
        {
            var directory = Path.Combine(package.Directory, "zero", "rules");

            if (!Directory.Exists(directory)) continue;

            foreach (var file in Directory.EnumerateFiles(directory, "*.md", SearchOption.AllDirectories).Order())
                rules.Add(Parse(package, File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
        }

        return [.. rules.OrderBy(r => r.Package, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal)];
    }

    /// <summary>Reads the YAML frontmatter a rule file opens with, then returns the body.</summary>
    private static RuleFile Parse(InstalledPackage package, string text, string fallbackId)
    {
        var id = fallbackId;
        var title = fallbackId;
        var enforcedBy = new List<string>();
        var appliesTo = new List<string>();
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
                        case "applies-to":
                            appliesTo.AddRange(value.Trim('[', ']')
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                .Select(v => v.Trim('"', '\'')));
                            break;
                        case "enforced-by":
                            enforcedBy.AddRange(value.Trim('[', ']')
                                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                            break;
                    }
                }

                body = text[(end + 4)..].TrimStart('\n');
            }
        }

        return new RuleFile(package.Id, package.Version, id, title, enforcedBy, appliesTo, body.TrimEnd());
    }
}

/// <summary>A failure the user can act on. Reported without a stack trace.</summary>
internal sealed class ZeroToolException(string message) : Exception(message);
