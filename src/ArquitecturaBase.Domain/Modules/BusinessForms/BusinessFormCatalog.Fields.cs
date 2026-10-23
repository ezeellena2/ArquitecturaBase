using System.Text.Json;
using System.Text.RegularExpressions;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.BusinessForms;

public sealed record BusinessFormCatalogField(Guid Id, string Key, IReadOnlyDictionary<string, string> Name,
    string Type, Guid? SourceCatalogId, bool Required, bool Unique, string? Min, string? Max, int? MaxLength,
    string Role, int Position, bool IsActive)
{
    public static Guid CreateIdentity() => Guid.CreateVersion7();
}

public sealed partial class BusinessFormCatalog
{
    public const int MaxFields = 20;
    public string? FieldsJson { get; private set; }
    public string? PresentationJson { get; private set; }

    /// <summary>Updates configuration inside the caller's existing catalog revision boundary.</summary>
    public Result SetFields(IReadOnlyList<BusinessFormCatalogField> fields, string? presentationJson)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (Kind != BusinessFormCatalogKind.Manual) return FormCatalogErrors.ManualItemsUnavailable;
        if (fields.Count > MaxFields) return FormCatalogErrors.TooManyFields;
        var stored = FieldsJson is null ? [] : JsonSerializer.Deserialize<BusinessFormCatalogField[]>(FieldsJson, JsonSerializerOptions.Web)!;
        var byId = stored.ToDictionary(field => field.Id);
        if (byId.Keys.Any(id => fields.All(field => field.Id != id))) return FormCatalogErrors.FieldInvalid;
        var ids = new HashSet<Guid>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var canonical = new List<BusinessFormCatalogField>();
        foreach (var field in fields)
        {
            if (!keys.Add(field.Key)) return FormCatalogErrors.FieldKeyDuplicate;
            if (field.Id == Guid.Empty || !ids.Add(field.Id) || !FieldKeyPattern().IsMatch(field.Key)
                || field.Name.Count == 0 || !field.Name.TryGetValue("es", out var name) || string.IsNullOrWhiteSpace(name)
                || field.Name.Any(pair => pair.Key is not ("es" or "en") || pair.Value.Length > 120 || !StorableText.IsStorable(pair.Value))
                || field.Type is not ("text" or "integer" or "decimal" or "date" or "boolean" or "catalog")
                || field.Role is not ("data" or "filter") || field.Position < 0
                || (field.Type == "catalog") != (field.SourceCatalogId is not null)
                || field.SourceCatalogId == Guid.Empty
                || field.MaxLength is < 1 or > 300 || field.Type != "text" && field.MaxLength is not null
                || field.Type is "text" or "boolean" or "catalog" && (field.Min is not null || field.Max is not null))
                return FormCatalogErrors.FieldInvalid;
            if (byId.TryGetValue(field.Id, out var original))
            {
                if (original.Key != field.Key) return FormCatalogErrors.FieldInvalid;
                canonical.Add(field.IsActive ? field : field with { Name = original.Name });
            }
            else
            {
                if (field.Required) return FormCatalogErrors.FieldInvalid;
                canonical.Add(field);
            }
        }
        FieldsJson = canonical.Count == 0 ? null : JsonSerializer.Serialize(canonical, JsonSerializerOptions.Web);
        PresentationJson = presentationJson;
        return Result.Success();
    }

    public Result SetManualItemFields(BusinessFormCatalogItem item, string fieldValuesJson)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldValuesJson);
        if (Kind != BusinessFormCatalogKind.Manual) return FormCatalogErrors.ManualItemsUnavailable;
        if (item.CatalogId != Id) return FormCatalogErrors.ItemNotFound;
        item.SetFields(fieldValuesJson);
        return Result.Success();
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,39}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex FieldKeyPattern();
}
