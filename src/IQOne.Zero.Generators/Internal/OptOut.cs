using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Generators.Internal;

/// <summary>
/// Whether the author asked for nothing to be generated here.
/// </summary>
/// <remarks>
/// One marker for every generator, because it says one thing: generated code cannot be edited,
/// so its user has to be able to decline it. Checked on the target and on the assembly, and the
/// assembly is how a project declines all of it at once.
/// </remarks>
internal static class OptOut
{
    private const string Name = "IQOne.Zero.NoGenerateAttribute";

    /// <summary>Whether this symbol, or the assembly it is in, declines generation.</summary>
    public static bool Declared(ISymbol? symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingSymbol)
        {
            if (Carries(current)) return true;

            // Containing types as well as the assembly: a nested type inside a declined one is
            // declined, which is what somebody marking the outer type meant.
            if (current is IAssemblySymbol) break;
        }

        return symbol?.ContainingAssembly is { } assembly && Carries(assembly);
    }

    /// <summary>Whether the assembly being compiled declines generation.</summary>
    public static bool Declared(Compilation compilation) => Carries(compilation.Assembly);

    private static bool Carries(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == Name) return true;

        return false;
    }
}
