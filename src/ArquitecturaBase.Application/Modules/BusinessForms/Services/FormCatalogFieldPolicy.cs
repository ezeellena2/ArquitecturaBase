using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static partial class FormCatalogFieldPolicy
{
    public static Result<JsonElement?> Normalize(BusinessFormCatalogField field, string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value)) return field.Required ? FormCatalogErrors.FieldValueRequired : Result.Success<JsonElement?>(null);
        object canonical;
        switch (field.Type)
        {
            case "text":
                if (value.Length > (field.MaxLength ?? 300) || !StorableText.IsStorable(value)) return FormCatalogErrors.FieldValueInvalid;
                canonical = value;
                break;
            case "integer":
            case "decimal":
            case "date":
                var normalized = FormCatalogValuePolicy.Normalize(field.Type switch
                {
                    "integer" => BusinessFormCatalogValueType.Integer,
                    "decimal" => BusinessFormCatalogValueType.Decimal,
                    _ => BusinessFormCatalogValueType.Date,
                }, value);
                if (normalized.IsFailure || !InBounds(field, normalized.Value!)) return FormCatalogErrors.FieldValueInvalid;
                canonical = field.Type == "integer" ? long.Parse(normalized.Value!, CultureInfo.InvariantCulture) : normalized.Value!;
                break;
            case "boolean":
                if (value is not ("true" or "false")) return FormCatalogErrors.FieldValueInvalid;
                canonical = value == "true";
                break;
            case "catalog":
                if (!Guid.TryParseExact(value, "D", out var itemId) || itemId == Guid.Empty) return FormCatalogErrors.FieldValueInvalid;
                canonical = itemId.ToString("D");
                break;
            default:
                return FormCatalogErrors.FieldValueInvalid;
        }
        return (JsonElement?)JsonSerializer.SerializeToElement(canonical, JsonSerializerOptions.Web);
    }

    public static Result<JsonElement?> NormalizeImport(BusinessFormCatalogField field, string? input, string culture, bool canonical)
    {
        if (field.Type is "integer" or "decimal" or "date" && !string.IsNullOrWhiteSpace(input))
        {
            var type = field.Type switch { "integer" => BusinessFormCatalogValueType.Integer, "decimal" => BusinessFormCatalogValueType.Decimal, _ => BusinessFormCatalogValueType.Date };
            var normalized = FormCatalogValuePolicy.NormalizeImport(type, input, culture, canonical);
            return normalized.IsFailure ? FormCatalogErrors.FieldValueInvalid : Normalize(field, normalized.Value);
        }
        if (field.Type == "boolean" && !canonical)
            input = input?.Trim().ToLowerInvariant() switch { "sí" or "si" or "yes" or "1" => "true", "no" or "0" => "false", var other => other };
        return Normalize(field, input);
    }

    public static Dictionary<string, JsonElement> ReadValues(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog field values are invalid.");

    public static string? Input(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        _ => value.GetRawText(),
    };

    public static IReadOnlyList<BusinessFormCatalogField> ReadFields(string? json) => json is null ? [] :
        JsonSerializer.Deserialize<BusinessFormCatalogField[]>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored catalog fields are invalid.");

    public static bool ValidRules(BusinessFormCatalogField field)
    {
        if (field.Type is not ("integer" or "decimal" or "date")) return field.Min is null && field.Max is null;
        var withoutBounds = field with { Required = false, Min = null, Max = null };
        if (field.Min is not null && (Normalize(withoutBounds, field.Min) is var minimum && (minimum.IsFailure || minimum.Value is null))) return false;
        if (field.Max is not null && (Normalize(withoutBounds, field.Max) is var maximum && (maximum.IsFailure || maximum.Value is null))) return false;
        return field.Min is null || field.Max is null || Compare(field.Type, field.Min, field.Max) <= 0;
    }

    public static BusinessFormCatalogField CanonicalRules(BusinessFormCatalogField field)
    {
        var withoutBounds = field with { Min = null, Max = null, Required = false };
        return field with
        {
            Min = field.Min is null ? null : Input(Normalize(withoutBounds, field.Min).Value!.Value),
            Max = field.Max is null ? null : Input(Normalize(withoutBounds, field.Max).Value!.Value),
        };
    }

    private static bool InBounds(BusinessFormCatalogField field, string value) =>
        (field.Min is null || Compare(field.Type, value, field.Min) >= 0) && (field.Max is null || Compare(field.Type, value, field.Max) <= 0);

    private static int Compare(string type, string left, string right) => type == "date"
        ? string.CompareOrdinal(left, right)
        : decimal.Parse(left, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(right, CultureInfo.InvariantCulture));
}
