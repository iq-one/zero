using System.Collections.Immutable;
using IQOne.Zero.Generators.Registration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.Generators.Tests.Harness;

/// <summary>
/// Runs the generator over a real compilation and returns both the generated source and
/// the diagnostics, so a test asserts on what a consumer's build would actually see.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    public static GeneratorRun Run(string source, string assemblyName = "Test.Module")
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create(
                generators: [new ServiceRegistrationGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: null,
                optionsProvider: new TestOptionsProvider())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out _);

        var result = driver.GetRunResult().Results.Single();

        return new GeneratorRun(
            [.. result.Diagnostics],
            result.GeneratedSources.Length == 0
                ? string.Empty
                : result.GeneratedSources[0].SourceText.ToString());
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var locations = new HashSet<string>(trusted, StringComparer.OrdinalIgnoreCase);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (!assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                locations.Add(assembly.Location);

        // The generator treats an assembly as a module only when it references Zero's core,
        // so the test compilation has to reference it too.
        foreach (var type in new[]
                 {
                     typeof(Modules.IModule),
                     typeof(DependencyInjection.Descriptors.IScoped),
                     typeof(App.Application),
                     typeof(Zero.Messaging.ISender),
                     typeof(Zero.Result),
                     typeof(Zero.Web.GetAttribute),
                     typeof(Zero.Validation.IValidator)
                 })
            locations.Add(type.Assembly.Location);

        return [.. locations.Where(File.Exists).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))];
    }
}

internal sealed record GeneratorRun(ImmutableArray<Diagnostic> Diagnostics, string GeneratedSource)
{
    public IEnumerable<string> DiagnosticIds => Diagnostics.Select(d => d.Id);

    public bool HasError => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}

/// <summary>Minimal options provider. The generator reads no build properties.</summary>
internal sealed class TestOptionsProvider : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new Options();

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

    private sealed class Options : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = null!;
            return false;
        }
    }
}
