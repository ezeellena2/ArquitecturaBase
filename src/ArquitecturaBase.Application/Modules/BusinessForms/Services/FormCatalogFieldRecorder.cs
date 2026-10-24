using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormCatalogFieldRecorder(IFormCatalogReader reader)
{
    public async Task<Result<FormCatalogFieldPreparation>> PrepareAsync(BusinessFormCatalog catalog,
        IReadOnlyList<BusinessFormCatalogItem> items, IReadOnlyList<FormCatalogFieldSaveRequest?>? inputs,
        FormCatalogPresentation? presentation, IReadOnlyList<Guid>? confirmedRemovals, CancellationToken ct)
    {
        var original = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        var byId = original.ToDictionary(field => field.Id);
        var resolved = new List<BusinessFormCatalogField>();
        var identityMap = new Dictionary<string, Guid>(StringComparer.Ordinal);
        if (inputs is null) resolved.AddRange(original);
        else
        {
            if (inputs.Count > BusinessFormCatalog.MaxFields) return FieldErrors.On(FormCatalogErrors.TooManyFields, "fields");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var seen = new HashSet<Guid>();
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                var path = $"fields[{index}]";
                if (input is null || input.Name is null || input.Type is null || input.Role is null || string.IsNullOrEmpty(input.Key))
                    return FieldErrors.On(FormCatalogErrors.FieldInvalid, path);
                if (!keys.Add(input.Key)) return FieldErrors.On(FormCatalogErrors.FieldKeyDuplicate, path + ".key");
                BusinessFormCatalogField? prior = null;
                if (input.Id is { } supplied && (!byId.TryGetValue(supplied, out prior) || !seen.Add(supplied)))
                    return FieldErrors.On(FormCatalogErrors.FieldInvalid, path + ".id");
                if (prior is not null && prior.Key != input.Key) return FieldErrors.On(FormCatalogErrors.FieldInvalid, path + ".key");
                if (prior is null && (string.IsNullOrWhiteSpace(input.LocalId) || input.Required))
                    return FieldErrors.On(FormCatalogErrors.FieldInvalid, path + (input.Required ? ".required" : ".localId"));
                var id = prior?.Id ?? BusinessFormCatalogField.CreateIdentity();
                var names = input.Name.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value!.Trim(), StringComparer.Ordinal);
                var field = new BusinessFormCatalogField(id, input.Key, names, input.Type, input.SourceCatalogId,
                    input.Required, input.Unique, input.Min, input.Max, input.MaxLength, input.Role, input.Position, input.IsActive);
                if (!FormCatalogFieldPolicy.ValidRules(field)) return FieldErrors.On(FormCatalogErrors.FieldInvalid, path + ".type");
                field = FormCatalogFieldPolicy.CanonicalRules(field);
                if (prior is null && !identityMap.TryAdd(input.LocalId!, id)) return FieldErrors.On(FormCatalogErrors.FieldInvalid, path + ".localId");
                resolved.Add(field);
            }
            if (original.Any(field => !seen.Contains(field.Id))) return FieldErrors.On(FormCatalogErrors.FieldInvalid, "fields");
        }
        var removedIds = resolved.Where(field => !field.IsActive && byId.TryGetValue(field.Id, out var old) && old.IsActive).Select(field => field.Id);
        var changedIds = resolved.Where(field => field.IsActive && byId.TryGetValue(field.Id, out var old) && old.IsActive && ChangedProperty(old, field) is not null)
            .Select(field => field.Id);
        var checkedIds = removedIds.Concat(changedIds).ToArray();
        var fieldUses = checkedIds.Length == 0 ? [] : await reader.ListFieldUsageAsync(catalog.Id, checkedIds, ct);
        var usesByField = fieldUses.ToLookup(use => use.FieldId);
        var culture = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        for (var index = 0; index < resolved.Count; index++)
        {
            var field = resolved[index];
            if (!byId.TryGetValue(field.Id, out var prior) || !prior.IsActive) continue;
            if (field.IsActive)
            {
                // A filter's match needs the field's role, type and source as they are; a question that only shows the field does not.
                var filters = usesByField[field.Id].Where(use => use.ByMatch).ToArray();
                if (filters.Length > 0 && ChangedProperty(prior, field) is { } property) return InUse(Uses(filters), $"fields[{index}].{property}");
                continue;
            }
            var fieldKey = field.Id.ToString("D");
            if (presentation?.Title?.Contains(fieldKey) == true || presentation?.Description?.Contains(fieldKey) == true)
            {
                var names = FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson);
                return InUse([new(null, FormRecordPresentationPolicy.Text(names, culture), null, null, "Catalog")], $"fields[{index}].isActive");
            }
            var uses = Uses(usesByField[field.Id]);
            if (uses.Length > 0) return InUse(uses, $"fields[{index}].isActive");
            var count = items.Count(item => FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson).ContainsKey(field.Id.ToString("D")));
            if (count > 0 && confirmedRemovals?.Contains(field.Id) != true)
                return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldRemovalNotConfirmed,
                    new Dictionary<string, object?> { ["count"] = count }), $"fields[{index}].isActive");
        }
        if (!ValidPresentation(catalog, resolved, presentation)) return FieldErrors.On(FormCatalogErrors.PresentationInvalid, "presentation");
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var field in resolved.Where(field => field.IsActive && field.Type == "catalog"))
        {
            if (field.SourceCatalogId is not { } sourceId || sourceId == catalog.Id) return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"fields[{resolved.IndexOf(field)}].sourceCatalogId");
            if (sources.ContainsKey(sourceId)) continue;
            var source = await reader.FindAsync(sourceId, ct);
            if (source is null || !source.IsActive || source.Kind != "Manual") return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"fields[{resolved.IndexOf(field)}].sourceCatalogId");
            sources.Add(sourceId, await reader.ListAllItemsAsync(sourceId, ct));
        }
        // A catalog field that changes type or source converts its saved options by their text, so its previous source is read
        // too, inactive options included; it is only read for those texts, so it need not be active.
        foreach (var field in resolved.Where(field => field.IsActive && byId.TryGetValue(field.Id, out var old) && old.Type == "catalog"
            && (field.Type != old.Type || field.SourceCatalogId != old.SourceCatalogId)))
        {
            var previousSourceId = byId[field.Id].SourceCatalogId!.Value;
            if (!sources.ContainsKey(previousSourceId)) sources.Add(previousSourceId, await reader.ListAllItemsAsync(previousSourceId, ct));
        }
        var stored = catalog.SetFields(resolved, presentation is null ? null : JsonSerializer.Serialize(presentation, JsonSerializerOptions.Web));
        if (stored.IsFailure) return FieldErrors.On(stored.Error, "fields");
        var canonical = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        return new FormCatalogFieldPreparation(new(canonical, sources,
            canonical.Where(field => byId.TryGetValue(field.Id, out var old) && (field.Type != old.Type || field.SourceCatalogId != old.SourceCatalogId)).Select(field => field.Id).ToHashSet(),
            canonical.Where(field => field.IsActive && field.Required && byId.TryGetValue(field.Id, out var old) && !old.Required).Select(field => field.Id).ToHashSet(),
            byId, culture), identityMap);
    }

    private static string? ChangedProperty(BusinessFormCatalogField prior, BusinessFormCatalogField field) =>
        field.Role != prior.Role ? "role" : field.Type != prior.Type ? "type" : field.SourceCatalogId != prior.SourceCatalogId ? "sourceCatalogId" : null;

    private static FormCatalogFieldUsageResponse[] Uses(IEnumerable<FormCatalogFieldUsageRow> rows) =>
        rows.Select(use => new FormCatalogFieldUsageResponse(use.DefinitionId, use.FormName, use.Question, use.QuestionId, use.Stage)).ToArray();

    private static ValidationError InUse(IReadOnlyList<FormCatalogFieldUsageResponse> uses, string path) =>
        FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldInUse, new Dictionary<string, object?> { ["uses"] = uses }), path);

    public static bool ValidPresentation(BusinessFormCatalog catalog, IReadOnlyList<BusinessFormCatalogField> fields, FormCatalogPresentation? presentation) =>
        FormCatalogPresentationPolicy.ValidConfiguration(presentation, fields, catalog.ValueType.ToString());
}
