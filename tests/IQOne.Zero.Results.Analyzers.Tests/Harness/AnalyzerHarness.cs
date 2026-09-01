using System.Collections.Immutable;
using IQOne.Zero.Results.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.Results.Analyzers.Tests.Harness;

/// <summary>
/// Runs the analyzer over a real compilation, so a test asserts on what a consumer's build
/// would actually report rather than on a hand-built syntax tree.
/// </summary>
/// <remarks>
/// This project exists because two of the rules were wrong in ways only a run would show:
/// ZERO100 missed the discard form its own documentation used as the example, and ZERO102
/// was declared but never reported by anything.
/// </remarks>
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
            .WithAnalyzers([new ResultUsageAnalyzer()])
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

        // The analyzer does nothing in a compilation that does not reference the package.
        locations.Add(typeof(global::IQOne.Zero.Result).Assembly.Location);

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

    public IEnumerable<string> Messages => Reported.Select(d => d.GetMessage());
}
