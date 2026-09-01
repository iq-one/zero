using System.Collections.Immutable;
using IQOne.Zero.Regify.Internal;
using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Regify.Registration;

/// <summary>Extracts raw symbol facts. Interpretation happens during emission.</summary>
internal static class SymbolCollector
{
    private static readonly SymbolDisplayFormat Full = SymbolDisplayFormat.FullyQualifiedFormat;

    public static ServiceCandidate DescribeService(INamedTypeSymbol type, SyntaxNode node)
    {
        var attributes = ImmutableArray.CreateBuilder<AttributeUsage>();

        foreach (var attribute in type.GetAttributes())
        {
            var arguments = attribute.ConstructorArguments
                .SelectMany(a => a.Kind == TypedConstantKind.Array ? a.Values : [a])
                .Select(v => (v.Value as ITypeSymbol)?.ToDisplayString(Full) ?? v.Value as string ?? string.Empty)
                .Where(v => v.Length > 0);

            var named = attribute.NamedArguments
                .Select(n => $"{n.Key}={n.Value.Value}");

            attributes.Add(new AttributeUsage(
                attribute.AttributeClass?.OriginalDefinition.ToDisplayString() ?? string.Empty,
                new EquatableArray<string>([.. arguments, .. named])));
        }

        var dependencies = type.InstanceConstructors
            .Where(c => c.DeclaredAccessibility == Accessibility.Public)
            .OrderByDescending(c => c.Parameters.Length)
            .FirstOrDefault()
            ?.Parameters.Select(p => p.Type.OriginalDefinition.ToDisplayString(Full)).ToList() ?? [];

        return new ServiceCandidate(
            type.ToDisplayString(Full),
            !type.IsAbstract && !type.IsGenericType,
            new EquatableArray<string>([.. type.AllInterfaces.Select(i => i.OriginalDefinition.ToDisplayString())]),
            new EquatableArray<string>([.. type.Interfaces.Select(i => i.OriginalDefinition.ToDisplayString())]),
            type.Name,
            new EquatableArray<AttributeUsage>(attributes.ToImmutable()),
            new EquatableArray<string>([.. dependencies]),
            LocationInfo.From(node));
    }

    /// <summary>Open generic name without arity, for example <c>Ns.IServiceHandler</c>.</summary>
    private static string Open(INamedTypeSymbol type)
    {
        var name = type.OriginalDefinition.ToDisplayString();
        var index = name.IndexOf('<');

        return index < 0 ? name : name.Substring(0, index);
    }

    /// <summary>Applies the <c>FooRepository</c> to <c>IFooRepository</c> naming convention.</summary>
    public static string? DefaultInterface(string typeName, IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            var simple = candidate;
            var dot = simple.LastIndexOf('.');

            if (dot >= 0) simple = simple.Substring(dot + 1);

            if (simple.Length > 1 && simple[0] == 'I' &&
                typeName.EndsWith(simple.Substring(1), StringComparison.Ordinal))
                return candidate;
        }

        return null;
    }
}
