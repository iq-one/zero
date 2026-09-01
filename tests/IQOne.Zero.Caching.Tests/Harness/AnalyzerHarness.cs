using System.Collections.Immutable;
using IQOne.Zero.Caching.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.Caching.Tests.Harness;

/// <summary>
/// Runs the analyzer over a real compilation, so a test asserts on what a consumer's build
/// would actually report rather than on a hand-built syntax tree.
/// </summary>
internal static class AnalyzerHarness
{
    private static readonly ImmutableArray<MetadataReference> References = BuildReferences();

    public static async Task<AnalyzerRun> RunAsync(string source)
    {
        var compilation = CSharpCompilation.Create(
            "Test.Module",
            [CSharpSyntaxTree.ParseText(source)],
            References,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        var reported = await compilation
            .WithAnalyzers([new CacheableUsageAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(CancellationToken.None);

        return new AnalyzerRun(
            [.. reported],
            [.. compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error)]);
    }

    private static ImmutableArray<MetadataReference> BuildReferences()
    {
        var trusted = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        var locations = new HashSet<string>(trusted, StringComparer.OrdinalIgnoreCase);

        foreach (var type in new[] { typeof(ICacheable), typeof(Messaging.ISender), typeof(Result) })
            locations.Add(type.Assembly.Location);

        return [.. locations.Where(File.Exists).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))];
    }
}

/// <summary>
/// What the build saw.
/// </summary>
/// <remarks>
/// The compiler's own errors are carried alongside the analyzer's: a snippet with a typo in
/// it reports nothing, and a test that only looks at analyzer output would call that a pass.
/// </remarks>
/// <param name="Reported">What the analyzer reported.</param>
/// <param name="CompilerErrors">What the compiler rejected before the analyzer ran.</param>
internal sealed record AnalyzerRun(
    ImmutableArray<Diagnostic> Reported, ImmutableArray<Diagnostic> CompilerErrors)
{
    public IEnumerable<string> Ids => Reported.Select(d => d.Id);
}
