namespace IQOne.Zero.Testing;

/// <summary>
/// Turns what actually happened into the sentence an assertion message ends with.
/// </summary>
/// <remarks>
/// Kept in one place because the value of these helpers is entirely in their wording, and
/// wording drifts when each assertion writes its own.
/// </remarks>
internal static class Explain
{
    /// <summary>Renders a failure's reasons, one numbered line each.</summary>
    internal static string Errors(ErrorList errors)
        => errors.Count == 0
            // A default(Result) is a failure with no errors. Saying so directly saves the
            // reader from hunting for the error nobody ever set.
            ? "it failed carrying no errors at all, which is what an uninitialised default result looks like"
            : $"it failed with {errors.Count} {(errors.Count == 1 ? "error" : "errors")}:{Environment.NewLine}"
              + string.Join(Environment.NewLine, errors.Select((error, index) => $"  [{index + 1}] {error}"));

    /// <summary>Renders a value so that an empty string or a null is unmistakable.</summary>
    internal static string Value(object? value) => value switch
    {
        null => "null",
        string text => $"\"{text}\"",
        _ => value.ToString() is { Length: > 0 } text ? text : value.GetType().Name
    };

    /// <summary>Renders a list of codes, names or types as a quoted, comma-separated list.</summary>
    internal static string List(IEnumerable<string> items)
        => string.Join(", ", items.Select(item => $"'{item}'")) is { Length: > 0 } joined ? joined : "(none)";
}
