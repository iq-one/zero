using System.Text.Json;
using System.Text.Json.Serialization;

namespace IQOne.Zero.Web.Binding;

/// <summary>
/// Reads a boolean that arrived as text.
/// </summary>
/// <remarks>
/// <para>
/// Route and query values reach the serializer as JSON strings — that is what overlaying
/// them means — and <c>"true"</c> is not a JSON boolean. Numbers survive the trip because
/// <c>JsonSerializerDefaults.Web</c> allows reading them from a string; booleans and enums
/// have no such setting, so <c>?includePaid=true</c> failed on every request.
/// </para>
/// <para>
/// A converter rather than sniffing the text and emitting a real JSON <c>true</c>: sniffing
/// decides from the value what the target type must be, so <c>?q=true</c> or <c>?code=42</c>
/// bound to a <see cref="string"/> would stop working. The type is what should decide, and
/// only the serializer knows it.
/// </para>
/// </remarks>
internal sealed class TextBooleanConverter : JsonConverter<bool>
{
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String => FromText(reader.GetString()),
            _ => throw new JsonException("A boolean was expected.")
        };

    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
        => writer.WriteBooleanValue(value);

    /// <remarks>
    /// <c>1</c> and <c>0</c> are accepted alongside the words because that is what a query
    /// string carries in practice; anything else is a mistake worth reporting rather than
    /// guessing at.
    /// </remarks>
    private static bool FromText(string? text) => text switch
    {
        "1" => true,
        "0" => false,
        _ when bool.TryParse(text, out var parsed) => parsed,
        _ => throw new JsonException($"'{text}' is not a boolean; use true or false.")
    };
}
