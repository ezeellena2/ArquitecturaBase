using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.StructuredData;

/// <summary>
/// N27 and H25: the reason of a reading by name (camelCase), for the options that keep it so, before their enum converter: a name
/// this version does not know, which a newer one writes, reads as <see cref="StructuredDocumentProblem.Other"/>. Only the reason:
/// it restricts nothing. Options that keep enums as numbers, as the engine's attachments, do not take it.
/// </summary>
public sealed class StructuredDocumentProblemJsonConverter : JsonConverter<StructuredDocumentProblem>
{
    public override StructuredDocumentProblem Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("The reason of a reading is a name.");
        return Enum.TryParse<StructuredDocumentProblem>(reader.GetString(), ignoreCase: true, out var problem) && Enum.IsDefined(problem)
            && !int.TryParse(reader.GetString(), out _) ? problem : StructuredDocumentProblem.Other;
    }

    public override void Write(Utf8JsonWriter writer, StructuredDocumentProblem value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    }
}
