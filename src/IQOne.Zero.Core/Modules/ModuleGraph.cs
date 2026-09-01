using System.Text;
using IQOne.Zero.Modules;

namespace IQOne.Zero.Modules;

/// <summary>Renders the resolved module order and its dependency edges.</summary>
public static class ModuleGraph
{
    /// <summary>Renders the resolved order and its edges, for startup logging and tests.</summary>
    /// <param name="orderedModules">The modules in resolved order.</param>
    /// <returns>A human-readable description.</returns>
    public static string Describe(IReadOnlyList<IModule> orderedModules)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Module order (derived from dependencies):");

        for (var index = 0; index < orderedModules.Count; index++)
        {
            var module = orderedModules[index];

            var dependencies = module.Dependencies.Count == 0
                ? "-"
                : string.Join(", ", module.Dependencies.Select(d => d.Namespace));

            builder.AppendLine($"  {index + 1,2}. {module.Name,-45} <- {dependencies}");
        }

        return builder.ToString().TrimEnd();
    }
}
