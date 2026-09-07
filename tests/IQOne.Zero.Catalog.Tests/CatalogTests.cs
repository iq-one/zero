using System.Text.Json;

namespace IQOne.Zero.Catalog.Tests;

/// <summary>
/// Keeps the catalog honest.
/// </summary>
/// <remarks>
/// The catalog is what tells a coding agent which capabilities exist, and an agent that
/// reads a stale catalog either misses a capability and writes its own, or reaches for a
/// package that was never published. Both are worse than having no catalog, because both
/// look like the framework working. So the catalog is checked against the packages
/// themselves on every build.
/// </remarks>
public class CatalogTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private static string Repository
    {
        get
        {
            for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, "IQOne.Zero.slnx")))
                    return d.FullName;

            throw new InvalidOperationException("The repository root was not found from the test output folder.");
        }
    }

    private static string Source => Path.Combine(Repository, "src");

    private static JsonElement Catalog =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Source, "IQOne.Zero", "zero", "catalog.json")))
            .RootElement;

    private static IEnumerable<(string Package, JsonElement Manifest)> Manifests()
    {
        foreach (var manifest in Directory.EnumerateFiles(Source, "capability.json", SearchOption.AllDirectories))
        {
            var package = new DirectoryInfo(manifest).Parent!.Parent!.Name;
            yield return (package, JsonDocument.Parse(File.ReadAllText(manifest)).RootElement);
        }
    }

    [Fact]
    public void Every_published_package_declares_a_capability_manifest()
    {
        var packages = Directory
            .EnumerateFiles(Source, "*.csproj", SearchOption.AllDirectories)
            // Not by name: an analyzer project is folded into the package it belongs to, and
            // the criterion for "is published" is whether the project packs at all.
            .Where(p => !File.ReadAllText(p).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            // The metapackage carries the catalog; the tool is not a capability.
            .Where(p => p is not ("IQOne.Zero" or "IQOne.Zero.Tool"))
            .Order(StringComparer.Ordinal);

        var described = Manifests().Select(m => m.Package).Order(StringComparer.Ordinal);

        described.Should().BeEquivalentTo(packages,
            "a package with no manifest is invisible to an agent reading the catalog");
    }

    [Fact]
    public void The_catalog_lists_exactly_the_packages_that_declare_a_manifest()
    {
        var listed = Catalog.GetProperty("capabilities").EnumerateArray()
            .Select(c => c.GetProperty("package").GetString()!)
            .Order(StringComparer.Ordinal);

        var described = Manifests().Select(m => m.Package).Order(StringComparer.Ordinal);

        listed.Should().BeEquivalentTo(described,
            "the catalog must not advertise a package that does not exist, nor omit one that does");
    }

    [Fact]
    public void Every_manifest_says_what_it_is_for()
    {
        foreach (var (package, manifest) in Manifests())
        {
            manifest.GetProperty("id").GetString().Should().NotBeNullOrWhiteSpace(package);
            manifest.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace(package);
            manifest.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace(package);

            manifest.GetProperty("useWhen").GetString().Should().NotBeNullOrWhiteSpace(
                $"{package} must tell an agent when to reach for it, not only what it is");

            manifest.GetProperty("package").GetString().Should().Be(package);
        }
    }

    [Fact]
    public void Every_diagnostic_a_manifest_claims_has_a_documentation_page()
    {
        var documented = Directory
            .EnumerateFiles(Path.Combine(Repository, "docs", "rules"), "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (package, manifest) in Manifests())
            foreach (var diagnostic in manifest.GetProperty("diagnostics").EnumerateArray())
                documented.Should().Contain(diagnostic.GetString()!,
                    $"{package} advertises {diagnostic}, and its helpLinkUri points at that page");
    }

    [Fact]
    public void Every_diagnostic_falls_in_the_range_its_capability_was_given()
    {
        // Bu test var cunku tablo KAYDI: Persistence'in projeksiyon ve esleme kurallari
        // ZERO220-229'da yayinlandi, oysa tablo o araligin Caching'e ait oldugunu
        // soyluyordu. Kimse kontrol etmedigi icin kimse gormedi, ve yeni bir yetenek
        // eklerken ayni yere bir kez daha yazildi. Id'ler yeniden kullanilamadigi icin
        // duzeltilecek olan tabloydu — ve bir daha kaymamasi icin zorlanmasi gerekiyor.
        var ranges = Ranges();

        ranges.Should().NotBeEmpty("the contract's range table is what this test reads");

        var wrong = new List<string>();

        foreach (var (package, manifest) in Manifests())
        {
            if (!manifest.TryGetProperty("diagnostics", out var declared)) continue;

            var id = manifest.GetProperty("id").GetString()!;

            foreach (var diagnostic in declared.EnumerateArray().Select(d => d.GetString()!))
            {
                var number = int.Parse(diagnostic.AsSpan("ZERO".Length));

                var owner = ranges.FirstOrDefault(r => number >= r.From && number <= r.To);

                if (owner.Owner is null)
                {
                    wrong.Add($"{diagnostic} ({package}) falls in no declared range");

                    continue;
                }

                // Sahiplik metni bir liste olabilir ("Messaging, web"), o yuzden yetenegin
                // kimligi icinde ARANIYOR; kimlikteki tire de bosluk sayiliyor.
                if (!Mentions(owner.Owner, id))
                    wrong.Add(
                        $"{diagnostic} ({package}) is in ZERO{owner.From:000}–ZERO{owner.To:000}, " +
                        $"which the contract gives to '{owner.Owner}'");
            }
        }

        wrong.Should().BeEmpty(
            "a diagnostic outside its capability's range means either the id or the table is " +
            "wrong, and since ids are never reused it is almost always the table");
    }

    private static bool Mentions(string owner, string id)
    {
        var words = owner
            .Replace(":", " ")
            .Replace(",", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return id
            .Split('-')
            .All(part => words.Any(w => w.Equals(part, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The range table in the capability contract, as it is written.</summary>
    private static List<(int From, int To, string? Owner)> Ranges()
    {
        var found = new List<(int, int, string?)>();

        var contract = Path.Combine(Repository, "docs", "capability-contract.md");

        foreach (var line in File.ReadAllLines(contract))
        {
            // | ZERO200–ZERO219 | Caching |   — en tire (–), kisa cizgi degil.
            var match = System.Text.RegularExpressions.Regex.Match(
                line, @"^\|\s*ZERO(\d{3})[–-]ZERO(\d{3})\s*\|\s*(.+?)\s*\|$");

            if (!match.Success) continue;

            found.Add((
                int.Parse(match.Groups[1].Value),
                int.Parse(match.Groups[2].Value),
                match.Groups[3].Value));
        }

        return found;
    }
}
