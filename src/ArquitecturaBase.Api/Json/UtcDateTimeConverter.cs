using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Json;

/// <summary>
/// Fechas en la API (sección 6.3 del spec): la salida es ISO 8601 en UTC con "Z"; las entradas con offset
/// se convierten a UTC y las que no traen offset se rechazan (400). También cubre DateTime?.
/// </summary>
public sealed class UtcDateTimeConverter : JsonConverter<DateTime>
{
    private const string OutputFormat = "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;

        // RoundtripKind deja Kind=Unspecified cuando el texto no trae offset ni "Z".
        if (text is null
            || !DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            || parsed.Kind == DateTimeKind.Unspecified
            || !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
        {
            throw new JsonException("Dates must be ISO 8601 with an explicit offset, for example 2026-09-18T17:32:00Z.");
        }

        return withOffset.UtcDateTime;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),

            // Todo el código trabaja en UTC: un valor sin Kind se interpreta como UTC.
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        writer.WriteStringValue(utc.ToString(OutputFormat, CultureInfo.InvariantCulture));
    }
}
