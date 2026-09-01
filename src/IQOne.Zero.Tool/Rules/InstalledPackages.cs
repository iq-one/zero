using System.Text.Json;

namespace IQOne.Zero.Tool.Rules;

/// <summary>A Zero package the project restored, and where its contents are on disk.</summary>
internal sealed record InstalledPackage(string Id, string Version, string Directory);

/// <summary>
/// Finds the Zero packages a project actually restored.
/// </summary>
/// <remarks>
/// The restore assets file is the authority on which versions are in use, so everything
/// derived from it matches what the project builds against. A checked-in list would drift
/// the moment someone upgraded, and drift is the failure this whole design avoids.
/// </remarks>
internal static class InstalledPackages
{
    private const string Prefix = "IQOne.Zero";

    public static IReadOnlyList<InstalledPackage> Find(string assetsFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(assetsFile));
        var root = document.RootElement;

        var folders = root.TryGetProperty("packageFolders", out var packageFolders)
            ? packageFolders.EnumerateObject().Select(f => f.Name).ToList()
            : [];

        if (folders.Count == 0)
            throw new ZeroToolException(
                $"'{assetsFile}' names no package folder. Run 'dotnet restore' and try again.");

        var found = new List<InstalledPackage>();

        if (!root.TryGetProperty("libraries", out var libraries)) return found;

        foreach (var library in libraries.EnumerateObject())
        {
            // Keys are "Package.Id/1.2.3".
            var separator = library.Name.LastIndexOf('/');
            if (separator < 0) continue;

            var id = library.Name[..separator];
            var version = library.Name[(separator + 1)..];

            if (!id.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) continue;

            var directory = folders
                .Select(folder => Path.Combine(folder, id.ToLowerInvariant(), version))
                .FirstOrDefault(System.IO.Directory.Exists);

            if (directory is not null) found.Add(new InstalledPackage(id, version, directory));
        }

        return [.. found.OrderBy(p => p.Id, StringComparer.Ordinal)];
    }
}
