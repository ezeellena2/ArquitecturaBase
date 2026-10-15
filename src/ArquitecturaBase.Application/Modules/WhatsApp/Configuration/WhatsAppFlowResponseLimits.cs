using System.Text.Json;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

public static class WhatsAppFlowResponseLimits
{
    public static bool HasFileLists(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject()
                .Any(property => property.Name.StartsWith("field_", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.Array);
        }
        catch (JsonException) { return false; }
    }
}
