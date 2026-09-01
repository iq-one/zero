using System.Text.Json;
using IQOne.Zero.Tool.Rules;

namespace IQOne.Zero.Tool.Capabilities;

/// <summary>Reads the capability manifests and the catalog out of the installed packages.</summary>
internal static class CapabilityReader
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static IReadOnlyList<Capability> Installed(IEnumerable<InstalledPackage> packages)
    {
        var found = new List<Capability>();

        foreach (var package in packages)
        {
            var manifest = Path.Combine(package.Directory, "zero", "capability.json");

            if (!File.Exists(manifest)) continue;

            var capability = Read<Capability>(manifest);

            if (capability is not null) found.Add(capability with { Version = package.Version });
        }

        return [.. found.OrderBy(c => c.Kind == "kernel" ? 0 : 1).ThenBy(c => c.Title, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The full published catalog, from whichever installed package carries it.
    /// </summary>
    /// <remarks>
    /// Returns null when the metapackage is not referenced. The individual packages still
    /// describe themselves, so the output degrades to "what is installed" rather than failing.
    /// </remarks>
    public static Catalog? Full(IEnumerable<InstalledPackage> packages)
        => packages
            .Select(p => Path.Combine(p.Directory, "zero", "catalog.json"))
            .Where(File.Exists)
            .Select(Read<Catalog>)
            .FirstOrDefault(c => c is not null);

    private static T? Read<T>(string path) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json);
        }
        catch (JsonException exception)
        {
            throw new ZeroToolException($"'{path}' could not be read: {exception.Message}");
        }
    }
}
