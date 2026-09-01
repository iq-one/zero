using Microsoft.CodeAnalysis;

namespace IQOne.Zero.Guidance.Tests;

/// <summary>
/// Every example the framework ships is compiled against the framework.
/// </summary>
/// <remarks>
/// <para>
/// Zero's premise is that guidance shipped inside a package cannot drift from the code it
/// describes. Shipping it in the package is only half of that; this is the other half.
/// </para>
/// <para>
/// It exists because the drift happened. Four blocking defects were found by review, and
/// all four were guidance describing an API that did not exist: an async <c>Ensure</c> that
/// was never written, an <c>Error.Failure</c> factory that was missing, a conversion from
/// <c>ErrorList</c> that does not exist, and an open-generic behaviour that fails the build.
/// Every one of them would have failed here on the day it was written.
/// </para>
/// </remarks>
public class GuidanceCompilesTests
{
    /// <summary>Every capability manifest's example, as origin and code.</summary>
    public static TheoryData<string, string> ManifestExamples()
    {
        var data = new TheoryData<string, string>();

        foreach (var snippet in GuidanceSource.ManifestExamples()) data.Add(snippet.Origin, snippet.Code);

        return data;
    }

    /// <summary>Every fenced C# block in a packaged rule file, as origin and code.</summary>
    public static TheoryData<string, string> RuleSnippets()
    {
        var data = new TheoryData<string, string>();

        foreach (var snippet in GuidanceSource.RuleSnippets()) data.Add(snippet.Origin, snippet.Code);

        return data;
    }

    [Theory]
    [MemberData(nameof(ManifestExamples))]
    public void A_capability_example_matches_the_real_api(string origin, string code)
        => Check(new Snippet(origin, code));

    [Theory]
    [MemberData(nameof(RuleSnippets))]
    public void A_rule_snippet_matches_the_real_api(string origin, string code)
        => Check(new Snippet(origin, code));

    [Fact]
    public void There_is_guidance_to_check()
    {
        // Guards the harness itself: a regex that stops matching would turn every test above
        // into a silent pass, which is the failure mode a checker must not have.
        GuidanceSource.ManifestExamples().Should().HaveCountGreaterThan(4);
        GuidanceSource.RuleSnippets().Should().HaveCountGreaterThan(10);
    }

    private static void Check(Snippet snippet)
    {
        var failures = GuidanceCompiler.Check(snippet);

        if (failures.Count == 0) return;

        Assert.Fail(
            $"""
             {snippet.Origin} describes an API the framework does not have.

             {string.Join("\n", failures.Select(Describe))}

             Fix the code or fix the guidance. Guidance that lies is worse than none: it is
             what an agent copies verbatim.

             --- snippet ---
             {snippet.Code}
             """);
    }

    private static string Describe(Diagnostic diagnostic)
        => $"  {diagnostic.Id}: {diagnostic.GetMessage()}";
}
