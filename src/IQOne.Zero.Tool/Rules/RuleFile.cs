namespace IQOne.Zero.Tool.Rules;

/// <summary>One rule file, as it ships inside a Zero package.</summary>
/// <param name="Package">The package that carries it.</param>
/// <param name="Version">That package's version, so the output can be traced to it.</param>
/// <param name="Id">Stable rule identifier from the file's frontmatter.</param>
/// <param name="Title">One-line summary from the frontmatter.</param>
/// <param name="EnforcedBy">Analyzer diagnostics that enforce this rule, if any.</param>
/// <param name="Body">The rule text, frontmatter removed.</param>
internal sealed record RuleFile(
    string Package,
    string Version,
    string Id,
    string Title,
    IReadOnlyList<string> EnforcedBy,
    string Body)
{
    /// <summary>
    /// Filename-safe form of <see cref="Id"/>, without the leading <c>zero.</c> that the
    /// written filename adds back as a prefix.
    /// </summary>
    public string Slug =>
        (Id.StartsWith("zero.", StringComparison.Ordinal) ? Id[5..] : Id).Replace('.', '-');
}
