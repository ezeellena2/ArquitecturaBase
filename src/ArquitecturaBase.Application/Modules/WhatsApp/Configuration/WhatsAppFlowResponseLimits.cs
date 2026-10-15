using System.Text.Json;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Configuration;

public static class WhatsAppFlowResponseLimits
{
    public static bool HasFileLists(string json) => CountFiles(json).HasValue;

    public static int? CountFiles(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var arrays = document.RootElement.EnumerateObject().Where(property => property.Name.StartsWith("field_", StringComparison.Ordinal)
                && property.Value.ValueKind == JsonValueKind.Array).ToArray();
            return arrays.Length == 0 ? null : arrays.Sum(property => property.Value.GetArrayLength());
        }
        catch (JsonException) { return null; }
    }
}
