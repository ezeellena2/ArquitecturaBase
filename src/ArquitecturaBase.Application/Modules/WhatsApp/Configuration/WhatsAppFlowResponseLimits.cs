using System.Globalization;
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
                && property.Value.ValueKind == JsonValueKind.Array
                && property.Value.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Object)).ToArray();
            return arrays.Length == 0 ? null : arrays.Sum(property => property.Value.GetArrayLength());
        }
        catch (JsonException) { return null; }
    }

    public static bool HasRows(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.EnumerateObject().Any(property => IsFieldKey(property.Name, out var row) && row.HasValue);
        }
        catch (JsonException) { return false; }
    }

    // field_<question>, or field_<question>_r<n> in a block: _r0 answers «¿Agregás un renglón?» and the rows go up to the
    // channel limit.
    public static bool IsFieldKey(string name, out int? row)
    {
        row = null;
        if (name.Length < 38 || !name.StartsWith("field_", StringComparison.Ordinal) || !Guid.TryParseExact(name.AsSpan(6, 32), "N", out _))
            return false;
        if (name.Length == 38) return true;
        var suffix = name.AsSpan(38);
        if (!suffix.StartsWith("_r", StringComparison.Ordinal) || !int.TryParse(suffix[2..], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            || value > WhatsAppFlowProfile.MaxRows || suffix.Length != (value < 10 ? 3 : 4))
            return false;
        row = value;
        return true;
    }
}
