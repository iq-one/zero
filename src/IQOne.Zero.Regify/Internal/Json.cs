using System.Text;

namespace IQOne.Zero.Regify.Internal;

/// <summary>
/// Minimal JSON reader. Analyzers target netstandard2.0 and bundling a JSON library
/// risks assembly version conflicts inside the IDE.
/// </summary>
internal sealed class JsonValue
{
    public Dictionary<string, JsonValue> Object { get; } = new(StringComparer.Ordinal);
    public string? String { get; set; }
    public double? Number { get; set; }
    public bool? Boolean { get; set; }
    public bool IsObject { get; set; }

    public JsonValue? Get(string key) => Object.TryGetValue(key, out var v) ? v : null;

    public string? GetString(string key) => Get(key)?.String;

    public int? GetInt(string key) => Get(key)?.Number is { } n ? (int)n : null;

    public bool GetBool(string key) => Get(key)?.Boolean ?? false;
}

internal static class Json
{
    public static JsonValue? Parse(string text)
    {
        var index = 0;
        try { return ParseValue(text, ref index); }
        catch { return null; }
    }

    private static JsonValue ParseValue(string s, ref int i)
    {
        SkipWhitespace(s, ref i);

        return s[i] switch
        {
            '{' => ParseObject(s, ref i),
            '[' => ParseArray(s, ref i),
            '"' => new JsonValue { String = ParseString(s, ref i) },
            't' => ParseLiteral(s, ref i, "true", new JsonValue { Boolean = true }),
            'f' => ParseLiteral(s, ref i, "false", new JsonValue { Boolean = false }),
            'n' => ParseLiteral(s, ref i, "null", new JsonValue()),
            _ => ParseNumber(s, ref i)
        };
    }

    private static JsonValue ParseObject(string s, ref int i)
    {
        var result = new JsonValue { IsObject = true };
        i++; // {

        SkipWhitespace(s, ref i);

        if (s[i] == '}') { i++; return result; }

        while (true)
        {
            SkipWhitespace(s, ref i);
            var key = ParseString(s, ref i);

            SkipWhitespace(s, ref i);
            i++; // :

            result.Object[key] = ParseValue(s, ref i);

            SkipWhitespace(s, ref i);

            if (s[i] == ',') { i++; continue; }

            i++; // }
            return result;
        }
    }

    // Arrays are not part of the schema format.
    private static JsonValue ParseArray(string s, ref int i)
    {
        var depth = 0;

        do
        {
            if (s[i] == '[') depth++;
            else if (s[i] == ']') depth--;
            i++;
        } while (depth > 0);

        return new JsonValue();
    }

    private static string ParseString(string s, ref int i)
    {
        var builder = new StringBuilder();
        i++; // "

        while (s[i] != '"')
        {
            if (s[i] == '\\')
            {
                i++;
                builder.Append(s[i] switch
                {
                    'n' => '\n', 't' => '\t', 'r' => '\r',
                    'b' => '\b', 'f' => '\f', _ => s[i]
                });
            }
            else builder.Append(s[i]);

            i++;
        }

        i++; // "
        return builder.ToString();
    }

    private static JsonValue ParseNumber(string s, ref int i)
    {
        var start = i;

        while (i < s.Length && (char.IsDigit(s[i]) || s[i] is '-' or '+' or '.' or 'e' or 'E')) i++;

        return new JsonValue
        {
            Number = double.TryParse(
                s.Substring(start, i - start),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var value) ? value : null
        };
    }

    private static JsonValue ParseLiteral(string s, ref int i, string literal, JsonValue value)
    {
        i += literal.Length;
        return value;
    }

    private static void SkipWhitespace(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
