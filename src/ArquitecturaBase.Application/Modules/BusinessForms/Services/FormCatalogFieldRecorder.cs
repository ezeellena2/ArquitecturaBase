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
        if (!ValidPresentation(catalog, resolved, presentation)) return FieldErrors.On(FormCatalogErrors.PresentationInvalid, "presentation");
        for (var index = 0; index < resolved.Count; index++)
        {
            var field = resolved[index];
            if (field.IsActive || !byId.TryGetValue(field.Id, out var prior) || !prior.IsActive) continue;
            var uses = await reader.FindFieldUsageAsync(catalog.Id, field.Id, ct);
            if (uses.Count > 0) return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldInUse,
                new Dictionary<string, object?> { ["uses"] = uses }), $"fields[{index}].isActive");
            var count = items.Count(item => FormCatalogFieldPolicy.ReadValues(item.FieldValuesJson).ContainsKey(field.Id.ToString("D")));
            if (count > 0 && confirmedRemovals?.Contains(field.Id) != true)
                return FieldErrors.OnWithMetadata(FormCatalogErrors.FieldDetails(FormCatalogErrors.FieldRemovalNotConfirmed,
                    new Dictionary<string, object?> { ["count"] = count }), $"fields[{index}].isActive");
        }
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        foreach (var field in resolved.Where(field => field.IsActive && field.Type == "catalog"))
        {
            if (field.SourceCatalogId is not { } sourceId || sourceId == catalog.Id) return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"fields[{resolved.IndexOf(field)}].sourceCatalogId");
            if (sources.ContainsKey(sourceId)) continue;
            var source = await reader.FindAsync(sourceId, ct);
            if (source is null || !source.IsActive || source.Kind != "Manual") return FieldErrors.On(FormCatalogErrors.FieldInvalid, $"fields[{resolved.IndexOf(field)}].sourceCatalogId");
            sources.Add(sourceId, await reader.ListAllItemsAsync(sourceId, ct));
        }
        var stored = catalog.SetFields(resolved, presentation is null ? null : JsonSerializer.Serialize(presentation, JsonSerializerOptions.Web));
        if (stored.IsFailure) return FieldErrors.On(stored.Error, "fields");
        var canonical = FormCatalogFieldPolicy.ReadFields(catalog.FieldsJson);
        return new FormCatalogFieldPreparation(new(canonical, sources,
            canonical.Where(field => byId.TryGetValue(field.Id, out var old) && (field.Type != old.Type || field.SourceCatalogId != old.SourceCatalogId)).Select(field => field.Id).ToHashSet(),
            canonical.Where(field => field.IsActive && field.Required && byId.TryGetValue(field.Id, out var old) && !old.Required).Select(field => field.Id).ToHashSet()), identityMap);
    }

    public static bool ValidPresentation(BusinessFormCatalog catalog, IReadOnlyList<BusinessFormCatalogField> fields, FormCatalogPresentation? presentation)
    {
        if (presentation is null) return true;
        if (presentation.Title is not { Count: > 0 and <= 24 } || presentation.Description is null || presentation.Description.Count > 24) return false;
        var allowed = fields.Where(field => field.IsActive).Select(field => field.Id.ToString("D")).ToHashSet(StringComparer.Ordinal);
        allowed.UnionWith(["text", "code", "detail"]);
        if (catalog.ValueType != BusinessFormCatalogValueType.Text) allowed.Add("value");
        return presentation.Title.Concat(presentation.Description).All(allowed.Contains);
    }
}
