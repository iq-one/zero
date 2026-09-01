using System.Text;
using IQOne.Zero.Tool.Capabilities;

namespace IQOne.Zero.Tool.Rules;

/// <summary>
/// Writes what an agent needs to work inside Zero: the catalog of what exists, and the
/// rules for using it.
/// </summary>
/// <remarks>
/// Everything goes inside a marked block that is rewritten in place, so re-running after an
/// upgrade never destroys what a person wrote around it.
/// </remarks>
internal static class RuleWriter
{
    /// <summary>Where the full rule texts are written, and where AGENTS.md points.</summary>
    private const string RulesFolder = ".zero/rules";

    private const string Begin = "<!-- BEGIN IQOne.Zero -->";
    private const string End = "<!-- END IQOne.Zero -->";

    // Matched by prefix so a block written by an older version is replaced rather than
    // duplicated. Renaming a marker must never leave two blocks behind.
    private const string BeginPrefix = "<!-- BEGIN IQOne.Zero";
    private const string EndPrefix = "<!-- END IQOne.Zero";

    /// <summary>
    /// Reports which files <see cref="Write"/> would change, without changing them.
    /// </summary>
    /// <remarks>
    /// The other half of shipping guidance inside packages. Travelling with the package
    /// keeps the rules and the code at one version; nothing yet keeps the copy checked into
    /// a repository at that version too. An upgrade that is not followed by
    /// <c>zero rules init</c> leaves an agent reading last release's rules — and the file
    /// looks perfectly current, because it is a file somebody committed on purpose.
    /// </remarks>
    /// <param name="directory">The repository to check.</param>
    /// <param name="capabilities">Capabilities from the restored packages.</param>
    /// <param name="catalog">The published catalog, when the metapackage is referenced.</param>
    /// <param name="rules">Rules from the restored packages.</param>
    /// <returns>Paths that are missing or out of date. Empty when the repository is current.</returns>
    public static IReadOnlyList<string> Check(
        string directory,
        IReadOnlyList<Capability> capabilities,
        Catalog? catalog,
        IReadOnlyList<RuleFile> rules)
    {
        var stale = new List<string>();

        Compare(Path.Combine(directory, "AGENTS.md"), Compose(capabilities, catalog, rules));
        Compare(Path.Combine(directory, "CLAUDE.md"), "@AGENTS.md\n");

        var cursor = Path.Combine(directory, ".cursor", "rules");

        foreach (var rule in rules)
            CompareWhole(Path.Combine(cursor, $"zero-{rule.Slug}.mdc"), CursorRule(rule));

        var texts = Path.Combine(directory, RulesFolder);

        foreach (var rule in rules) CompareWhole(Path.Combine(texts, $"{rule.Slug}.md"), RuleText(rule));

        if (Directory.Exists(texts))
        {
            var current = rules.Select(r => $"{r.Slug}.md").ToHashSet(StringComparer.Ordinal);

            stale.AddRange(Directory
                .EnumerateFiles(texts, "*.md")
                .Where(f => !current.Contains(Path.GetFileName(f)))
                .Select(f => $"{Path.GetRelativePath(directory, f)} (no longer shipped by any package)"));
        }

        // A rule removed upstream must stop being applied, so a leftover file is drift too.
        if (Directory.Exists(cursor))
        {
            var expected = rules.Select(r => $"zero-{r.Slug}.mdc").ToHashSet(StringComparer.Ordinal);

            stale.AddRange(Directory
                .EnumerateFiles(cursor, "zero-*.mdc")
                .Where(f => !expected.Contains(Path.GetFileName(f)))
                .Select(f => $"{Path.GetRelativePath(directory, f)} (no longer shipped by any package)"));
        }

        return stale;

        void Compare(string path, string content)
        {
            var expected = $"{Begin}\n{content.TrimEnd()}\n{End}\n";

            if (!File.Exists(path))
            {
                stale.Add($"{Path.GetRelativePath(directory, path)} (missing)");
                return;
            }

            var existing = File.ReadAllText(path).Replace("\r\n", "\n");
            var start = existing.IndexOf(BeginPrefix, StringComparison.Ordinal);
            var finish = existing.LastIndexOf(EndPrefix, StringComparison.Ordinal);

            if (start < 0 || finish <= start)
            {
                stale.Add($"{Path.GetRelativePath(directory, path)} (no Zero block)");
                return;
            }

            var afterEnd = existing.IndexOf("-->", finish, StringComparison.Ordinal);
            var block = afterEnd < 0 ? existing[start..] : existing[start..(afterEnd + 3)] + "\n";

            if (!string.Equals(block, expected, StringComparison.Ordinal))
                stale.Add($"{Path.GetRelativePath(directory, path)} (out of date)");
        }

        void CompareWhole(string path, string content)
        {
            if (!File.Exists(path))
                stale.Add($"{Path.GetRelativePath(directory, path)} (missing)");
            else if (!string.Equals(File.ReadAllText(path).Replace("\r\n", "\n"), content, StringComparison.Ordinal))
                stale.Add($"{Path.GetRelativePath(directory, path)} (out of date)");
        }
    }

    public static IReadOnlyList<string> Write(
        string directory,
        IReadOnlyList<Capability> capabilities,
        Catalog? catalog,
        IReadOnlyList<RuleFile> rules)
    {
        var written = new List<string>();

        var agents = Path.Combine(directory, "AGENTS.md");
        WriteBlock(agents, Compose(capabilities, catalog, rules));
        written.Add(agents);

        // Claude Code reads CLAUDE.md; an import keeps one source of truth.
        var claude = Path.Combine(directory, "CLAUDE.md");
        WriteBlock(claude, "@AGENTS.md\n");
        written.Add(claude);

        var texts = Path.Combine(directory, RulesFolder);
        Directory.CreateDirectory(texts);

        foreach (var stale in Directory.EnumerateFiles(texts, "*.md")) File.Delete(stale);

        foreach (var rule in rules)
        {
            var path = Path.Combine(texts, $"{rule.Slug}.md");
            File.WriteAllText(path, RuleText(rule));
            written.Add(path);
        }

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

    private static string Compose(
        IReadOnlyList<Capability> capabilities, Catalog? catalog, IReadOnlyList<RuleFile> rules)
    {
        var builder = new StringBuilder();

        builder.AppendLine("# Zero");
        builder.AppendLine();

        if (catalog is { Purpose.Length: > 0 })
            builder.AppendLine(catalog.Purpose).AppendLine();

        builder.AppendLine("This file is generated by `zero rules init` from the Zero packages this project");
        builder.AppendLine("references. Do not edit inside the marked block; anything outside it is kept.");
        builder.AppendLine();

        if (catalog is { ReadThisFirst.Length: > 0 })
        {
            builder.AppendLine("## Before you write a foundational abstraction");
            builder.AppendLine();
            builder.AppendLine(catalog.ReadThisFirst);
            builder.AppendLine();
        }

        AppendInstalled(builder, capabilities);
        AppendAvailable(builder, capabilities, catalog);
        AppendRules(builder, rules);

        return builder.ToString();
    }

    private static void AppendInstalled(StringBuilder builder, IReadOnlyList<Capability> capabilities)
    {
        if (capabilities.Count == 0) return;

        builder.AppendLine("## What this project already has");
        builder.AppendLine();
        builder.AppendLine("| Capability | Use it for | Turn it on with |");
        builder.AppendLine("| --- | --- | --- |");

        foreach (var capability in capabilities)
        {
            var entry = capability.EntryPoint is null ? "—" : $"`{capability.EntryPoint}`";
            builder.AppendLine($"| {capability.Title} | {capability.UseWhen} | {entry} |");
        }

        builder.AppendLine();

        foreach (var capability in capabilities)
        {
            builder.Append("### ").AppendLine(capability.Title);
            builder.AppendLine();
            builder.AppendLine(capability.Summary);
            builder.AppendLine();
            var facts = new List<string> { $"Package `{capability.Package}` {capability.Version}" };

            if (capability.KeyTypes.Count > 0)
                facts.Add($"Types you touch: {string.Join(", ", capability.KeyTypes.Select(t => $"`{t}`"))}");

            if (capability.Diagnostics.Count > 0)
                facts.Add($"Enforced by {string.Join(", ", capability.Diagnostics)}");

            builder.AppendLine(string.Join(". ", facts) + ".");

            if (capability.Example is { Length: > 0 })
            {
                builder.AppendLine();
                builder.AppendLine("```csharp");
                builder.AppendLine(capability.Example.TrimEnd());
                builder.AppendLine("```");
            }

            builder.AppendLine();
        }
    }

    private static void AppendAvailable(
        StringBuilder builder, IReadOnlyList<Capability> installed, Catalog? catalog)
    {
        if (catalog is null) return;

        var have = installed.Select(c => c.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = catalog.Capabilities.Where(c => !have.Contains(c.Package)).ToList();

        if (missing.Count == 0) return;

        builder.AppendLine("## What Zero also offers, not installed here");
        builder.AppendLine();
        builder.AppendLine("Reach for one of these before writing your own. Add it with");
        builder.AppendLine("`dotnet add package <package>`, then make its single `Add` call — and say so, rather");
        builder.AppendLine("than adding a dependency silently.");
        builder.AppendLine();
        builder.AppendLine("| Package | Use it for |");
        builder.AppendLine("| --- | --- |");

        foreach (var capability in missing)
            builder.AppendLine($"| `{capability.Package}` | {capability.UseWhen} |");

        builder.AppendLine();
    }

    /// <summary>
    /// Lists the rules and where to read them, rather than reproducing them.
    /// </summary>
    /// <remarks>
    /// This file is loaded at the start of every agent session, so its length is a tax paid
    /// before any work begins. The catalog above has to be here — an agent cannot look up a
    /// capability it does not know exists. A rule's full text does not: it is needed only
    /// while working in that area, and it sits at a path named right here.
    /// </remarks>
    private static void AppendRules(StringBuilder builder, IReadOnlyList<RuleFile> rules)
    {
        if (rules.Count == 0) return;

        builder.AppendLine("## Rules");
        builder.AppendLine();
        builder.AppendLine("Not style preferences. Most are enforced by analyzers, so breaking one fails the");
        builder.AppendLine($"build. Read the full text of any of these before working in its area — they are in");
        builder.AppendLine($"`{RulesFolder}/`, written there by `zero rules init` from the packages themselves.");
        builder.AppendLine();
        builder.AppendLine("| Rule | Enforced by | Read |");
        builder.AppendLine("| --- | --- | --- |");

        foreach (var rule in rules)
        {
            var enforced = rule.EnforcedBy.Count > 0 ? string.Join(", ", rule.EnforcedBy) : "convention";

            builder.AppendLine($"| {rule.Title} | {enforced} | `{RulesFolder}/{rule.Slug}.md` |");
        }

        builder.AppendLine();
    }

    /// <summary>
    /// Pushes a rule's own headings below the heading it is nested under, so the composed
    /// file has one coherent outline instead of a rule's "## Do" competing with "## Rules".
    /// </summary>
    private static string Demote(string body)
    {
        var lines = body.Split('\n');
        var inFence = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("```", StringComparison.Ordinal)) inFence = !inFence;
            else if (!inFence && lines[i].StartsWith("#", StringComparison.Ordinal)) lines[i] = "#" + lines[i];
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// One rule as Cursor reads it, scoped when the rule says where it applies.
    /// </summary>
    /// <remarks>
    /// A rule that declares <c>applies-to</c> gets <c>globs</c>, so Cursor loads it while
    /// the file being edited matches and leaves it out otherwise. Applying every rule to
    /// every request spends context on nine rules to deliver the one that matters.
    /// </remarks>
    private static string CursorRule(RuleFile rule)
    {
        var scope = rule.AppliesTo.Count > 0
            ? $"globs: {string.Join(",", rule.AppliesTo)}"
            : "alwaysApply: true";

        return $"""
                ---
                description: {rule.Title}
                {scope}
                ---

                {rule.Body}
                """;
    }

    /// <summary>One rule as a standalone page, with what enforces it stated up front.</summary>
    private static string RuleText(RuleFile rule)
    {
        var enforced = rule.EnforcedBy.Count > 0
            ? $"Enforced by {string.Join(", ", rule.EnforcedBy)}."
            : "A convention; nothing enforces it automatically.";

        return $"""
                # {rule.Title}

                *{enforced} From `{rule.Package}` {rule.Version}.*

                {rule.Body}
                """;
    }

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
        var start = existing.IndexOf(BeginPrefix, StringComparison.Ordinal);
        // Last, not first: a file left with two blocks by an older version collapses to one.
        var finish = existing.LastIndexOf(EndPrefix, StringComparison.Ordinal);

        if (start >= 0 && finish > start)
        {
            var afterEnd = existing.IndexOf("-->", finish, StringComparison.Ordinal);
            var tail = afterEnd < 0 ? string.Empty : existing[(afterEnd + 3)..].TrimStart('\n');

            File.WriteAllText(path, existing[..start] + block + tail);
            return;
        }

        File.WriteAllText(path, existing.TrimEnd() + "\n\n" + block);
    }
}
