using System.Text;

namespace IQOne.Zero.Tool.Rules;

/// <summary>
/// Writes the rules into the files coding agents read.
/// </summary>
/// <remarks>
/// Everything is written inside a marked block and rewritten in place on the next run, so
/// re-running after a version upgrade never destroys what a person wrote around it.
/// </remarks>
internal static class RuleWriter
{
    private const string Begin = "<!-- BEGIN IQOne.Zero rules -->";
    private const string End = "<!-- END IQOne.Zero rules -->";

    public static IReadOnlyList<string> Write(string directory, IReadOnlyList<RuleFile> rules)
    {
        var written = new List<string>();

        var agents = Path.Combine(directory, "AGENTS.md");
        WriteBlock(agents, Compose(rules));
        written.Add(agents);

        // Claude Code reads CLAUDE.md; an import keeps one source of truth.
        var claude = Path.Combine(directory, "CLAUDE.md");
        WriteBlock(claude, "@AGENTS.md\n");
        written.Add(claude);

        var cursor = Path.Combine(directory, ".cursor", "rules");
        Directory.CreateDirectory(cursor);

        // Clear what a previous run wrote, so a rule removed upstream stops being applied.
        foreach (var stale in Directory.EnumerateFiles(cursor, "zero-*.mdc")) File.Delete(stale);

        foreach (var rule in rules)
        {
            var path = Path.Combine(cursor, $"zero-{rule.Slug}.mdc");
            File.WriteAllText(path, CursorRule(rule));
            written.Add(path);
        }

        return written;
    }

    private static string Compose(IReadOnlyList<RuleFile> rules)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Zero");
        builder.AppendLine();
        builder.AppendLine("This project is built on Zero. The rules below are not style preferences —");
        builder.AppendLine("most of them are enforced by analyzers, so breaking one fails the build.");
        builder.AppendLine();
        builder.AppendLine("They were generated from the Zero packages this project references:");
        builder.AppendLine();

        foreach (var package in rules.GroupBy(r => (r.Package, r.Version)).OrderBy(g => g.Key.Package, StringComparer.Ordinal))
            builder.AppendLine($"- `{package.Key.Package}` {package.Key.Version}");

        builder.AppendLine();
        builder.AppendLine("Re-run `zero rules init` after upgrading. Do not edit inside this block by hand.");

        foreach (var rule in rules)
        {
            builder.AppendLine();
            builder.AppendLine("---");
            builder.AppendLine();
            builder.Append("### ").AppendLine(rule.Title);
            builder.AppendLine();

            if (rule.EnforcedBy.Count > 0)
                builder.AppendLine($"*Enforced by {string.Join(", ", rule.EnforcedBy)}.*").AppendLine();

            builder.AppendLine(rule.Body);
        }

        return builder.ToString();
    }

    private static string CursorRule(RuleFile rule) =>
        $"""
         ---
         description: {rule.Title}
         alwaysApply: true
         ---

         {rule.Body}
         """;

    /// <summary>Replaces the managed block, or appends one, leaving everything else intact.</summary>
    private static void WriteBlock(string path, string content)
    {
        var block = $"{Begin}\n{content.TrimEnd()}\n{End}\n";

        if (!File.Exists(path))
        {
            File.WriteAllText(path, block);
            return;
        }

        var existing = File.ReadAllText(path);
        var start = existing.IndexOf(Begin, StringComparison.Ordinal);
        var finish = existing.IndexOf(End, StringComparison.Ordinal);

        if (start >= 0 && finish > start)
        {
            File.WriteAllText(path, existing[..start] + block + existing[(finish + End.Length)..].TrimStart('\n'));
            return;
        }

        File.WriteAllText(path, existing.TrimEnd() + "\n\n" + block);
    }
}
