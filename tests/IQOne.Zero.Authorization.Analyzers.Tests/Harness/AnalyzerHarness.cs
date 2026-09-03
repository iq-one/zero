using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace IQOne.Zero.Authorization.Analyzers.Tests.Harness;

/// <summary>
/// Runs the analyzer over a real compilation, so a test asserts on what a consumer's build
/// would report rather than on a hand-built syntax tree.
/// </summary>
/// <remarks>
/// This project exists because ZERO450 had none. The rule reads the answer from the
/// attribute's ARGUMENTS, and an attribute that derives the answer instead — which 0.4.0
/// made possible and the changelog recommended — satisfied the rule while being reported by
/// it. Nothing caught that, because nothing ran the analyzer.
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
            .WithAnalyzers([new RequestAuthorizationAnalyzer()])
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

        foreach (var type in new[]
                 {
                     typeof(global::IQOne.Zero.Authorization.AuthorizeAttribute),
                     typeof(global::IQOne.Zero.Messaging.ISender),
                     typeof(global::IQOne.Zero.Web.PostAttribute),
                     typeof(global::IQOne.Zero.Result)
                 })
            locations.Add(type.Assembly.Location);

        return [.. locations.Where(File.Exists).Select(l => (MetadataReference)MetadataReference.CreateFromFile(l))];
    }
}

/// <summary>
/// What the build saw.
/// </summary>
/// <remarks>
/// The compiler's own errors are carried alongside: a snippet with a typo reports nothing,
/// and a test looking only at analyzer output would call that a pass.
/// </remarks>
internal sealed record AnalyzerRun(
    ImmutableArray<Diagnostic> Reported, ImmutableArray<Diagnostic> CompilerErrors)
{
    public IEnumerable<string> Ids => Reported.Select(d => d.Id);
}
