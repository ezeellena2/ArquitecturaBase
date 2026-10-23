using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormCatalogMatchPolicy
{
    public static bool HasValidShape(IReadOnlyList<FormCatalogMatch>? matches) => matches is null
        || matches.Count <= BusinessFormCatalog.MaxFields && matches.All(match => match is not null && match.FieldId != Guid.Empty
            && (match.Value is null) != (match.PersonAttributeId is null) && match.PersonAttributeId != Guid.Empty
            && (match.Value is null || !string.IsNullOrWhiteSpace(match.Value) && match.Value.Length <= 300 && StorableText.IsStorable(match.Value)))
            && matches.Select(match => match.FieldId).Distinct().Count() == matches.Count;

    public static Error? Validate(IReadOnlyList<FormCatalogMatch>? matches, IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyList<FormPersonAttributeStoredRow> attributes)
    {
        if (matches is null) return null;
        if (!HasValidShape(matches)) return FormPublicationErrors.MatchInvalid;
        var used = new HashSet<Guid>();
        foreach (var match in matches)
        {
            if (match is null || !used.Add(match.FieldId)) return FormPublicationErrors.MatchInvalid;
            var field = fields.SingleOrDefault(field => field.Id == match.FieldId);
            if (field is not { IsActive: true, Role: "filter" }
                || (match.Value is null) == (match.PersonAttributeId is null)) return FormPublicationErrors.MatchInvalid;
            if (match.PersonAttributeId is { } attributeId)
            {
                var attribute = attributes.SingleOrDefault(attribute => attribute.Id == attributeId);
                if (attribute is not { IsActive: true }) return FormPublicationErrors.MatchInvalid;
                if (field.Type != "catalog" || attribute.Type != "Catalog" || field.SourceCatalogId is null
                    || field.SourceCatalogId != attribute.SourceCatalogId) return FormPublicationErrors.MatchSourceMismatch;
            }
            else
            {
                var value = FormCatalogFieldPolicy.Normalize(field with { Required = true }, match.Value);
                if (value.IsFailure || value.Value is null) return FormPublicationErrors.MatchInvalid;
            }
        }
        return null;
    }

    public static Result<string?> Containment(IReadOnlyList<FormCatalogMatch>? matches, IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyList<FormPersonAttributeStoredRow> attributes, IReadOnlyDictionary<string, string>? personValues)
    {
        if (Validate(matches, fields, attributes) is not null) return FormRecordErrors.CatalogFilterUnavailable;
        if (matches is null || matches.Count == 0) return Result.Success<string?>(null);
        var containment = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var match in matches)
        {
            var field = fields.Single(field => field.Id == match.FieldId);
            var input = match.Value;
            if (match.PersonAttributeId is { } attributeId
                && (personValues is null || !personValues.TryGetValue(attributeId.ToString("D"), out input) || string.IsNullOrWhiteSpace(input)))
                return FormRecordErrors.RecipientDataMissing;
            var value = FormCatalogFieldPolicy.Normalize(field with { Required = true }, input);
            if (value.IsFailure || value.Value is null) return FormRecordErrors.CatalogFilterUnavailable;
            containment.Add(field.Id.ToString("D"), value.Value.Value);
        }
        return JsonSerializer.Serialize(containment, JsonSerializerOptions.Web);
    }

    public static Dictionary<string, string> RecipientValues(FormPersonRecipientDataRow data)
    {
        var values = FormDraftPolicy.Read<Dictionary<string, string>>(data.ValuesJson);
        foreach (var attribute in data.Attributes.Where(attribute => attribute.Type == "Catalog"))
        {
            var key = attribute.Id.ToString("D");
            if (values.TryGetValue(key, out var value) && (!Guid.TryParseExact(value, "D", out var itemId)
                || !data.OptionTexts.TryGetValue(itemId, out var option) || option.CatalogId != attribute.SourceCatalogId))
                values.Remove(key);
        }
        return values;
    }

    public static Result<string?> FixedContainment(IReadOnlyList<FormCatalogMatch>? matches, IReadOnlyList<BusinessFormCatalogField> fields)
    {
        var fixedMatches = matches?.Where(match => match.PersonAttributeId is null).ToArray();
        return Containment(fixedMatches, fields, [], null);
    }
}
