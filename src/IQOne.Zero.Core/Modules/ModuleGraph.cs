using System.Text;
using IQOne.Zero.Modules;

namespace IQOne.Zero.Modules;

/// <summary>Renders the resolved module order and its dependency edges.</summary>
public static class ModuleGraph
{
    public static string Describe(IReadOnlyList<IModule> orderedModules)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Modul sirasi (bagimliliklardan turetildi):");

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
