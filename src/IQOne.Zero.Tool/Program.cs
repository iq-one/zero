using IQOne.Zero.Tool.Capabilities;
using IQOne.Zero.Tool.Rules;

// The zero tool. It moves what ships inside the Zero packages — the capability catalog and
// the rule files — into the repository, where coding agents read them.
//
// Both travel in the packages so they cannot drift from the code they describe. This tool
// is the last step: into this repository, at the versions actually restored.

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

    var directory = Directory.GetCurrentDirectory();
    var packages = InstalledPackages.Find(FindAssets(directory));

    if (packages.Count == 0)
        throw new ZeroToolException(
            "This project references no Zero package. Add one with 'dotnet add package IQOne.Zero'.");

    var capabilities = CapabilityReader.Installed(packages);
    var catalog = CapabilityReader.Full(packages);
    var rules = RuleReader.Read(packages);

    return (args[0], args.Length > 1 ? args[1] : null) switch
    {
        ("rules", null or "init") => Init(directory, capabilities, catalog, rules),
        ("rules", "list") => ListRules(rules),
        ("capabilities", null or "list") => ListCapabilities(capabilities, catalog),
        var (command, action) => Unknown(command, action)
    };
}

static int Init(
    string directory,
    IReadOnlyList<Capability> capabilities,
    Catalog? catalog,
    IReadOnlyList<RuleFile> rules)
{
    foreach (var path in RuleWriter.Write(directory, capabilities, catalog, rules))
        Console.WriteLine(Path.GetRelativePath(directory, path));

    Console.WriteLine();
    Console.WriteLine($"{capabilities.Count} capability manifest(s) and {rules.Count} rule(s) written.");
    Console.WriteLine("Re-run after upgrading Zero or adding a package.");

    return 0;
}

static int ListRules(IReadOnlyList<RuleFile> rules)
{
    if (rules.Count == 0)
    {
        Console.WriteLine("No Zero package here carries rule files.");
        return 0;
    }

    foreach (var rule in rules)
    {
        var enforced = rule.EnforcedBy.Count > 0 ? $"  [{string.Join(" ", rule.EnforcedBy)}]" : string.Empty;
        Console.WriteLine($"{rule.Id,-42} {rule.Package} {rule.Version}{enforced}");
    }

    return 0;
}

static int ListCapabilities(IReadOnlyList<Capability> installed, Catalog? catalog)
{
    Console.WriteLine("Installed:");

    foreach (var capability in installed)
        Console.WriteLine($"  {capability.Package,-34} {capability.Title}");

    if (catalog is null) return 0;

    var have = installed.Select(c => c.Package).ToHashSet(StringComparer.OrdinalIgnoreCase);
    var missing = catalog.Capabilities.Where(c => !have.Contains(c.Package)).ToList();

    if (missing.Count == 0) return 0;

    Console.WriteLine();
    Console.WriteLine("Available, not installed:");

    foreach (var capability in missing)
        Console.WriteLine($"  {capability.Package,-34} {capability.Title}");

    return 0;
}

static int Unknown(string command, string? action)
{
    Console.Error.WriteLine(action is null
        ? $"zero: unknown command '{command}'."
        : $"zero: unknown action '{command} {action}'.");

    Usage();
    return 1;
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
          zero rules init        Write AGENTS.md, CLAUDE.md and .cursor/rules from the
                                 capability manifests and rule files inside the Zero
                                 packages this project uses. Content outside the managed
                                 block is kept.
          zero rules list        Show the rules those packages carry, and what enforces them.
          zero capabilities      Show what is installed, and what Zero offers that is not.

          zero --version
        """);
}
