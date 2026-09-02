using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace IQOne.Zero.Guidance.Tests;

/// <summary>
/// Compiles a guidance snippet against the framework as it is actually built.
/// </summary>
/// <remarks>
/// <para>
/// The snippets reference illustrative domain types — <c>Invoice</c>, <c>IInvoiceStore</c> —
/// that deliberately do not exist. Their absence is not the failure this looks for.
/// </para>
/// <para>
/// What it looks for is the framework API not matching what the guidance says: a method
/// that was never written, an extension that does not apply to that receiver, a handler
/// signature that does not satisfy its interface. Those produce a distinct set of error
/// codes, and those are the ones that fail the test. An unresolved domain name is ignored.
/// </para>
/// </remarks>
public static class GuidanceCompiler
{
    /// <summary>
    /// Errors that mean the guidance describes an API the framework does not have.
    /// </summary>
    /// <remarks>
    /// Chosen from the failures this actually caught: a missing async <c>Ensure</c> is
    /// CS1929, a missing <c>Error.Failure</c> is CS0117, returning an <c>ErrorList</c> where
    /// a <c>Result</c> is expected is CS0029, and a handler with the wrong return type is
    /// CS0738.
    ///
    /// Deliberately absent: CS0246 and CS0103, because an illustrative domain type is meant
    /// not to exist; and CS0122, because an invented name colliding with an internal type
    /// somewhere in the base library reports as inaccessible rather than missing.
    /// </remarks>
    private static readonly ImmutableHashSet<string> Fatal =
    [
        "CS0029", // cannot implicitly convert
        "CS0030", // cannot convert
        "CS0117", // no such member on the type
        "CS0311", // type argument does not satisfy its constraint
        "CS0535", // does not implement the interface member
        "CS0738", // implements the member with the wrong return type
        "CS1061", // no such member, and no accessible extension
        "CS1501", // no overload takes that many arguments
        "CS1503", // argument cannot convert
        "CS1620", // argument must be passed with a modifier
        "CS1929", // no such extension method for that receiver
        "CS7036"  // no argument for a required parameter
    ];

    private static readonly ImmutableArray<MetadataReference> References = Build();

    private static readonly string Usings = ComposeUsings();

    /// <summary>
    /// Imports every namespace the framework publishes, plus the few a snippet assumes.
    /// </summary>
    /// <remarks>
    /// Derived from the built assemblies rather than written out, so a new capability is
    /// covered the day it exists. A hand-maintained list would go stale and then report the
    /// staleness as a defect in the guidance — a checker whose own gaps look like findings
    /// is worse than no checker.
    /// </remarks>
    private static string ComposeUsings()
    {
        var probe = CSharpCompilation.Create("Probe", references: References);
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);

        Collect(probe.GlobalNamespace, string.Empty);

        void Collect(INamespaceSymbol symbol, string prefix)
        {
            foreach (var child in symbol.GetNamespaceMembers())
            {
                var name = prefix.Length == 0 ? child.Name : $"{prefix}.{child.Name}";

                if (!"IQOne".StartsWith(name.Split('.')[0], StringComparison.Ordinal) &&
                    !name.StartsWith("IQOne", StringComparison.Ordinal))
                    continue;

                if (child.GetTypeMembers().Any(t => t.DeclaredAccessibility == Accessibility.Public))
                    namespaces.Add(name);

                Collect(child, name);
            }
        }

        // Namespaces the framework's own public API names — DbContext, ModelBuilder,
        // HttpContext and so on. Derived rather than listed, for the same reason the IQOne
        // namespaces are: a hand-written list goes stale, and a stale list here does not
        // fail, it silently stops checking. Entity Framework was missing from the list this
        // replaces, so every snippet naming a DbContext went unverified.
        foreach (var name in DependencyNamespaces()) namespaces.Add(name);

        string[] assumed =
        [
            "System",
            "System.Collections.Generic",
            "System.ComponentModel.DataAnnotations",
            "System.Linq",
            "System.Threading",
            "System.Threading.Tasks"
        ];

        return string.Join("\n", assumed.Concat(namespaces).Select(n => $"using {n};"));
    }

    /// <summary>
    /// Every non-framework namespace the framework's public API mentions.
    /// </summary>
    /// <remarks>
    /// Walks the public surface of each <c>IQOne.Zero.*</c> assembly and collects the
    /// namespace of everything it names: base types, implemented interfaces, parameter and
    /// return types. If guidance is allowed to show a type, the framework's own API had to
    /// name it first.
    /// </remarks>
    private static SortedSet<string> DependencyNamespaces()
    {
        var probe = CSharpCompilation.Create("Probe", references: References);
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in probe.SourceModule.ReferencedAssemblySymbols)
        {
            if (!assembly.Name.StartsWith("IQOne.Zero", StringComparison.Ordinal)) continue;

            foreach (var type in PublicTypes(assembly.GlobalNamespace))
            {
                Note(type.BaseType);

                foreach (var contract in type.Interfaces) Note(contract);

                foreach (var member in type.GetMembers().Where(m => m.DeclaredAccessibility == Accessibility.Public))
                    switch (member)
                    {
                        case IMethodSymbol method:
                            Note(method.ReturnType);
                            foreach (var parameter in method.Parameters) Note(parameter.Type);
                            break;

                        case IPropertySymbol property:
                            Note(property.Type);
                            break;
                    }
            }
        }

        return found;

        void Note(ITypeSymbol? type)
        {
            if (type is null) return;

            if (type is INamedTypeSymbol named)
                foreach (var argument in named.TypeArguments) Note(argument);

            var space = type.ContainingNamespace;

            if (space is null || space.IsGlobalNamespace) return;

            var name = space.ToDisplayString();

            if (name.StartsWith("IQOne", StringComparison.Ordinal)) return;
            if (name.StartsWith("System", StringComparison.Ordinal)) return;

            found.Add(name);
        }
    }

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceSymbol space)
    {
        foreach (var type in space.GetTypeMembers())
            if (type.DeclaredAccessibility == Accessibility.Public)
                yield return type;

        foreach (var child in space.GetNamespaceMembers())
            foreach (var type in PublicTypes(child))
                yield return type;
    }

    /// <summary>Compiles one snippet and returns only the failures that matter.</summary>
    /// <param name="snippet">The snippet to check.</param>
    /// <returns>Diagnostics indicating a mismatch with the real API.</returns>
    public static IReadOnlyList<Diagnostic> Check(Snippet snippet)
    {
        var compilation = CSharpCompilation.Create(
            "Guidance",
            [CSharpSyntaxTree.ParseText(Wrap(snippet.Code))],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return
        [
            .. compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error && Fatal.Contains(d.Id))
        ];
    }

    /// <summary>
    /// Puts a snippet somewhere it can compile.
    /// </summary>
    /// <remarks>
    /// A snippet is either a set of declarations or a fragment of a method body. Parsing it
    /// first tells us which, so a fragment is not wrapped in a namespace and a declaration
    /// is not wrapped in a method.
    /// </remarks>
    private static string Wrap(string code)
    {
        var parsed = CSharpSyntaxTree.ParseText(code).GetRoot();

        var isDeclarations = parsed.ChildNodes().Any(n =>
            n is BaseTypeDeclarationSyntax or NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax);

        return isDeclarations
            ? $"{Usings}\nnamespace Guidance.Sample;\n\n{code}"
            : $$"""
                {{Usings}}
                namespace Guidance.Sample;

                internal static class Fragment
                {
                    internal static async Task Run(IServiceCollection services, CancellationToken cancellationToken)
                    {
                        await Task.Yield();
                {{code}}
                    }
                }
                """;
    }

    /// <summary>
    /// References the framework as it is built on disk, rather than as this project sees it.
    /// </summary>
    /// <remarks>
    /// By path, not by project reference: this test must not have to be edited every time a
    /// capability is added, and it must be able to check a package it does not depend on.
    /// </remarks>
    /// <summary>
    /// Where a type of this name is defined, across everything the checker can see.
    /// </summary>
    /// <remarks>
    /// Exists so the checker can be checked. A type it cannot resolve makes every snippet
    /// using that type report CS0246, which is ignored as an illustrative domain name, and
    /// the snippet silently stops being verified. A type it resolves TWICE is worse: the
    /// snippet binds to one definition and the framework to the other, and the mismatch is
    /// reported as a defect in guidance that is in fact correct.
    /// </remarks>
    public static IReadOnlyList<string> DefiningAssemblies(string metadataName)
    {
        var compilation = CSharpCompilation.Create("Probe", references: References);

        return [.. compilation
            .GetTypesByMetadataName(metadataName)
            .Select(t => t.ContainingAssembly.Identity.GetDisplayName())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    public static IReadOnlyList<string> AllDiagnostics(string code)
    {
        var tree = CSharpSyntaxTree.ParseText(Usings + code);
        var compilation = CSharpCompilation.Create(
            "Snippet", [tree], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return [.. compilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => $"{d.Id}: {d.GetMessage()}")];
    }

    private static ImmutableArray<MetadataReference> Build()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var locations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in trusted) locations[Path.GetFileNameWithoutExtension(path)] = path;

        // Everything this project references, whether or not it happens to be loaded.
        //
        // Loaded-assembly enumeration alone is not enough, and the way it fails is the worst
        // kind: an assembly nothing has touched yet is simply absent, its types resolve to
        // nothing, and every snippet that uses them reports CS0246 — which this checker
        // deliberately ignores as "an illustrative domain type". So the snippet is not
        // checked and the run is green. That is how a ConventionDbContext example with its
        // arguments in the wrong order shipped in 0.1.0.
        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
            locations[Path.GetFileNameWithoutExtension(path)] = path;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                locations[Path.GetFileNameWithoutExtension(assembly.Location)] = assembly.Location;

        var source = Path.Combine(GuidanceSource.Repository, "src");

        foreach (var built in Directory.EnumerateFiles(source, "IQOne.Zero*.dll", SearchOption.AllDirectories))
        {
            if (!built.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            if (built.Contains("net8.0")) continue;
            if (built.Contains(".Analyzers")) continue;

            locations[Path.GetFileNameWithoutExtension(built)] = built;
        }

        return [.. locations.Values.Where(File.Exists).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))];
    }
}
