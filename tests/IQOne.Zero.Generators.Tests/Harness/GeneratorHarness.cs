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
        => Run([source], assemblyName);

    /// <summary>
    /// Runs the generator over a compilation that does NOT reference the named assemblies.
    /// </summary>
    /// <remarks>
    /// Several of the generator's guarantees are about what it does <em>not</em> emit: an
    /// application with no events, or no web layer, must pay nothing for them. That cannot
    /// be tested against the default reference set, which carries every Zero assembly.
    /// </remarks>
    /// <param name="source">The file to compile.</param>
    /// <param name="without">Assembly names to leave out, for example <c>IQOne.Zero.Events</c>.</param>
    /// <returns>What the consumer's build would see.</returns>
    public static GeneratorRun RunWithout(string source, params string[] without)
    {
        var excluded = without.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var references = References
            .Where(r => !excluded.Contains(Path.GetFileNameWithoutExtension(r.Display) ?? string.Empty))
            .ToImmutableArray();

        return Run([source], "Test.Module", references);
    }

    /// <summary>
    /// Runs the generator over several files.
    /// </summary>
    /// <param name="sources">One entry per file. A partial type may be split across them.</param>
    /// <param name="assemblyName">The compilation's assembly name, which names the module.</param>
    /// <param name="extraReferences">Assemblies to reference in addition to the framework's own.</param>
    /// <returns>What the consumer's build would see.</returns>
    public static GeneratorRun Run(
        string[] sources,
        string assemblyName = "Test.Module",
        params MetadataReference[] extraReferences)
        => Run(sources, assemblyName, References.AddRange(extraReferences));

    private static GeneratorRun Run(
        string[] sources, string assemblyName, ImmutableArray<MetadataReference> references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            sources.Select(s => CSharpSyntaxTree.ParseText(s)),
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver
            .Create(
                generators: [new ServiceRegistrationGenerator().AsSourceGenerator()],
                additionalTexts: null,
                parseOptions: null,
                optionsProvider: new TestOptionsProvider())
            .RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _);

        var result = driver.GetRunResult().Results.Single();

        // Errors in the generated file only. The point of several of these rules is that a
        // mistake in generation surfaces as a compiler error in a file the developer never
        // wrote, so that is exactly what a test has to be able to see.
        var generatedErrors = updated.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error
                     && d.Location.SourceTree?.FilePath.EndsWith("Module.g.cs", StringComparison.Ordinal) == true);

        return new GeneratorRun(
            [.. result.Diagnostics],
            result.GeneratedSources.Length == 0
                ? string.Empty
                : result.GeneratedSources[0].SourceText.ToString(),
            [.. generatedErrors]);
    }

    /// <summary>
    /// Compiles <paramref name="source"/> into a reference, so a test can exercise the module
    /// discovery that reads a referenced assembly rather than the compilation being generated.
    /// </summary>
    /// <param name="source">The upstream assembly's source.</param>
    /// <param name="assemblyName">Its assembly name, which is what discovery looks up.</param>
    /// <returns>A reference to the compiled assembly.</returns>
    public static MetadataReference Reference(string source, string assemblyName)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();

        var emitted = compilation.Emit(stream);

        if (!emitted.Success)
            throw new InvalidOperationException(
                $"The upstream assembly did not compile: {string.Join("; ", emitted.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))}");

        return MetadataReference.CreateFromImage(stream.ToArray());
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
                     typeof(DependencyInjection.Annotations.ScopedAttribute),
                     typeof(App.Application),
                     typeof(Zero.Messaging.ISender),
                     typeof(Zero.Result),
                     typeof(Zero.Web.GetAttribute),
                     typeof(Zero.Validation.IValidator),
                     typeof(Zero.Events.IEvent),
                     typeof(Zero.Authorization.IRequirementHandler)
                 })
            locations.Add(type.Assembly.Location);

        return [.. locations.Where(File.Exists).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))];
    }
}

internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Diagnostics,
    string GeneratedSource,
    ImmutableArray<Diagnostic> GeneratedFileErrors)
{
    public IEnumerable<string> DiagnosticIds => Diagnostics.Select(d => d.Id);

    public bool HasError => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);

    /// <summary>Compiler errors raised by the generated file itself, as text.</summary>
    public IEnumerable<string> GeneratedFileErrorMessages => GeneratedFileErrors.Select(d => d.ToString());

    /// <summary>How many times <paramref name="text"/> appears in the generated file.</summary>
    public int Occurrences(string text)
    {
        var count = 0;

        for (var i = GeneratedSource.IndexOf(text, StringComparison.Ordinal); i >= 0;
             i = GeneratedSource.IndexOf(text, i + text.Length, StringComparison.Ordinal))
            count++;

        return count;
    }
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
