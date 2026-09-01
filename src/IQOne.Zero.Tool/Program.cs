using IQOne.Zero.Tool.Rules;

// The zero tool. One job today: take the rule files that ship inside the Zero packages a
// project references and write them where coding agents will read them.
//
// The rules travel in the packages so they cannot drift from the code they describe. This
// tool is what moves them the last step, into the repository, at the version in use.

try
{
    return Run(args);
}
catch (ZeroToolException exception)
{
    Console.Error.WriteLine($"zero: {exception.Message}");
    return 1;
}

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Usage();
        return args.Length == 0 ? 1 : 0;
    }

    if (args[0] is "--version")
    {
        Console.WriteLine(typeof(RuleFile).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");
        return 0;
    }

    if (args[0] != "rules")
    {
        Console.Error.WriteLine($"zero: unknown command '{args[0]}'.");
        Usage();
        return 1;
    }

    var action = args.Length > 1 ? args[1] : "init";
    var directory = Directory.GetCurrentDirectory();
    var rules = RuleReader.Read(FindAssets(directory));

    if (rules.Count == 0)
        throw new ZeroToolException(
            "No Zero package in this project carries rule files. " +
            "Check that IQOne.Zero is referenced and restored.");

    switch (action)
    {
        case "list":
            foreach (var rule in rules)
            {
                var enforced = rule.EnforcedBy.Count > 0 ? $"  [{string.Join(" ", rule.EnforcedBy)}]" : string.Empty;
                Console.WriteLine($"{rule.Id,-42} {rule.Package} {rule.Version}{enforced}");
            }

            return 0;

        case "init":
            foreach (var path in RuleWriter.Write(directory, rules))
                Console.WriteLine(Path.GetRelativePath(directory, path));

            Console.WriteLine();
            Console.WriteLine($"{rules.Count} rule(s) written. Re-run after upgrading Zero.");
            return 0;

        default:
            Console.Error.WriteLine($"zero: unknown action 'rules {action}'.");
            Usage();
            return 1;
    }
}

// Walks up from the working directory: the tool is normally run from a project folder, but
// running it from the repository root should still find the project underneath.
static string FindAssets(string directory)
{
    for (var current = new DirectoryInfo(directory); current is not null; current = current.Parent)
    {
        var assets = Path.Combine(current.FullName, "obj", "project.assets.json");

        if (File.Exists(assets)) return assets;

        var nested = Directory
            .EnumerateFiles(current.FullName, "project.assets.json", SearchOption.AllDirectories)
            .FirstOrDefault();

        if (nested is not null) return nested;
    }

    throw new ZeroToolException(
        "No restored project was found here. Run 'dotnet restore' first, " +
        "or run this from a folder containing the project.");
}

static void Usage()
{
    Console.WriteLine("""
        zero — the IQOne.Zero command line tool

        Usage:
          zero rules init     Write AGENTS.md, CLAUDE.md and .cursor/rules from the
                              rule files inside the Zero packages this project uses.
                              Existing content outside the managed block is kept.
          zero rules list     Show the rules those packages carry, and what enforces them.

          zero --version
        """);
}
