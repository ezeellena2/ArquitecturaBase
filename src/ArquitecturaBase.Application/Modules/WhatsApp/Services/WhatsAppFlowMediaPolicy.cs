using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal static class WhatsAppFlowMediaPolicy
{
    public static Result<IReadOnlyList<StructuredRecordFileInput>> Decode(WhatsAppFlowProjection projection,
        IReadOnlyDictionary<string, string> answers, IReadOnlyDictionary<string, string> media)
    {
        var fields = projection.Mapping.Where(field => field.Type is "photo" or "document").ToArray();
        if (media.Keys.Any(key => !fields.Any(field => field.Key == key))) return FileErrors.InvalidMetadata;
        var result = new List<StructuredRecordFileInput>();
        Guid? currentFieldId = null;
        try
        {
            foreach (var field in fields)
            {
                currentFieldId = field.FieldId;
                if (answers.ContainsKey(field.Key) || field.Files is not { MinFiles: >= 0, MaxFiles: <= 10 } settings
                    || settings.MaxFiles < 1 || settings.MinFiles > settings.MaxFiles) return FileErrors.AtField(FileErrors.InvalidMetadata, currentFieldId);
                if (!media.TryGetValue(field.Key, out var raw))
                {
                    if (settings.MinFiles > 0) return FileErrors.AtField(FileErrors.InvalidMetadata, currentFieldId);
                    continue;
                }
                using var json = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 3 });
                var array = json.RootElement;
                if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() < settings.MinFiles
                    || array.GetArrayLength() > settings.MaxFiles || result.Count + array.GetArrayLength() > 10) return FileErrors.AtField(FileErrors.InvalidMetadata, currentFieldId);
                var position = 0;
                foreach (var file in array.EnumerateArray())
                {
                    if (ReadMediaId(file) is not { Length: > 0 and <= 128 } id || Read(file, "file_name") is not { Length: > 0 and <= 255 } name
                        || Read(file, "mime_type") is not { Length: > 0 and <= 128 } type || Read(file, "sha256") is not { Length: > 0 and <= 128 } hash)
                        return FileErrors.AtField(FileErrors.InvalidMetadata, currentFieldId);
                    result.Add(new(field.FieldId, position++, id, field.Type, name, type, hash));
                }
            }
            return result;
        }
        catch (JsonException) { return FileErrors.AtField(FileErrors.InvalidMetadata, currentFieldId); }
    }
    public static string? QuestionLabel(WhatsAppFlowProjection projection, Guid? fieldId)
    {
        var key = projection.Mapping.SingleOrDefault(field => field.FieldId == fieldId)?.Key;
        if (key is null) return null;
        using var json = JsonDocument.Parse(projection.Json);
        return FindLabel(json.RootElement, key);
    }
    private static string? FindLabel(JsonElement element, string key)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (Read(element, "name") == key && Read(element, "label") is { } label) return label;
            foreach (var property in element.EnumerateObject())
                if (FindLabel(property.Value, key) is { } found) return found;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray())
                if (FindLabel(child, key) is { } found) return found;
        return null;
    }
    private static string? ReadMediaId(JsonElement file)
    {
        if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("id", out var id)) return null;
        var value = id.ValueKind switch
        {
            JsonValueKind.String => id.GetString(),
            JsonValueKind.Number => id.GetRawText(),
            _ => null,
        };
        return value is { Length: > 0 and <= 128 } && value.All(char.IsAsciiDigit) ? value : null;
    }

    private static string? Read(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
