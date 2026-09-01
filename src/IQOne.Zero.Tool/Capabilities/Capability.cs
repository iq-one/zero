using System.Text.Json.Serialization;

namespace IQOne.Zero.Tool.Capabilities;

/// <summary>
/// What one Zero package offers, as it ships inside that package.
/// </summary>
/// <remarks>
/// Rule files teach an agent how to use a capability correctly. This teaches it that the
/// capability exists at all — which is the more common failure. An agent that has never
/// heard of <c>IQOne.Zero.Caching</c> writes its own cache, and no analyzer can catch that,
/// because hand-rolling something the framework already has is not a rule violation.
/// </remarks>
internal sealed record Capability
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    /// <summary>"kernel" for the packages every application has, "capability" otherwise.</summary>
    [JsonPropertyName("kind")] public string Kind { get; init; } = "capability";

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    [JsonPropertyName("summary")] public string Summary { get; init; } = string.Empty;

    /// <summary>The decision rule: when this is the right thing to reach for.</summary>
    [JsonPropertyName("useWhen")] public string UseWhen { get; init; } = string.Empty;

    [JsonPropertyName("package")] public string Package { get; init; } = string.Empty;

    /// <summary>The single call that turns it on, or null when the package needs none.</summary>
    [JsonPropertyName("entryPoint")] public string? EntryPoint { get; init; }

    /// <summary>The types a consumer actually touches.</summary>
    [JsonPropertyName("keyTypes")] public IReadOnlyList<string> KeyTypes { get; init; } = [];

    /// <summary>Diagnostics that enforce correct use.</summary>
    [JsonPropertyName("diagnostics")] public IReadOnlyList<string> Diagnostics { get; init; } = [];

    /// <summary>One canonical snippet. Agents generalise from examples far better than from prose.</summary>
    [JsonPropertyName("example")] public string? Example { get; init; }

    [JsonIgnore] public string Version { get; init; } = string.Empty;
}

/// <summary>The full list of published capabilities, shipped in the metapackage.</summary>
internal sealed record Catalog
{
    [JsonPropertyName("framework")] public string Framework { get; init; } = "Zero";

    [JsonPropertyName("site")] public string Site { get; init; } = string.Empty;

    [JsonPropertyName("purpose")] public string Purpose { get; init; } = string.Empty;

    /// <summary>The instruction an agent should read before designing anything foundational.</summary>
    [JsonPropertyName("readThisFirst")] public string ReadThisFirst { get; init; } = string.Empty;

    [JsonPropertyName("capabilities")] public IReadOnlyList<CatalogEntry> Capabilities { get; init; } = [];
}

internal sealed record CatalogEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("kind")] public string Kind { get; init; } = "capability";

    [JsonPropertyName("package")] public string Package { get; init; } = string.Empty;

    [JsonPropertyName("title")] public string Title { get; init; } = string.Empty;

    [JsonPropertyName("useWhen")] public string UseWhen { get; init; } = string.Empty;
}
