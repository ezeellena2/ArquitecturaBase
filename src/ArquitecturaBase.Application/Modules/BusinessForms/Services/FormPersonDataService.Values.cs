using System.Text.Json;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed partial class FormPersonDataService
{
    private async Task<Result<FormPersonDataResponse>> SaveDataCoreAsync(Guid userId, UpdateFormPersonDataRequest request, CancellationToken ct)
    {
        await repository.LockAttributesAsync(ct);
        await repository.LockDataAsync(userId, ct);
        var allowed = await access.CheckValuesAsync(userId, ct);
        if (allowed.IsFailure) return allowed.Error;
        var attributes = await repository.ListAttributesAsync(ct);
        var data = await repository.GetDataAsync(userId, ct);
        if (data?.Revision != request.ExpectedRevision) return FormPersonDataErrors.RevisionConflict;
        var active = attributes.Where(attribute => attribute.IsActive).ToDictionary(attribute => attribute.Id.ToString("D"), StringComparer.Ordinal);
        var values = ReadValues(data?.ValuesJson ?? "{}");
        var requested = request.Values!;
        foreach (var key in requested.Keys)
            if (!active.ContainsKey(key)) return FieldErrors.On(FormPersonDataErrors.ValueInvalid, "values." + key);
        var sources = new Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>>();
        var sourceIds = requested.Where(pair => !string.IsNullOrWhiteSpace(pair.Value)).Select(pair => active[pair.Key].SourceCatalogId).OfType<Guid>().Distinct().Order().ToArray();
        foreach (var sourceId in sourceIds) await catalogs.LockAsync(sourceId, ct);
        foreach (var sourceId in sourceIds)
        {
            var source = await SourceAsync("Catalog", sourceId, ct);
            if (source.IsFailure) return source.Error;
            sources.Add(sourceId, await catalogReader.ListAllItemsAsync(sourceId, ct));
        }
        var optionTexts = data is null ? new Dictionary<Guid, string>()
            : new Dictionary<Guid, string>((await reader.FindRecipientDataAsync(userId, ct)).OptionTexts);
        foreach (var (key, input) in requested)
        {
            if (string.IsNullOrWhiteSpace(input)) { values.Remove(key); continue; }
            var attribute = active[key];
            var value = input.Trim();
            if (attribute.Type == BusinessFormPersonAttributeType.Catalog)
            {
                if (!Guid.TryParseExact(value, "D", out var id) || !sources.TryGetValue(attribute.SourceCatalogId!.Value, out var options)) return FieldErrors.On(FormPersonDataErrors.ValueInvalid, "values." + key);
                var option = options.SingleOrDefault(option => option.Id == id && option.IsActive);
                if (option is null) return FieldErrors.On(FormPersonDataErrors.ValueInvalid, "values." + key);
                value = id.ToString("D");
                optionTexts[id] = Culture() == "en" && !string.IsNullOrWhiteSpace(option.TextEn) ? option.TextEn : option.TextEs;
            }
            values[key] = value;
        }
        var json = JsonSerializer.Serialize(values, JsonSerializerOptions.Web);
        if (data is null) { data = BusinessFormPersonData.Create(userId, json); repository.Add(data); }
        else { var updated = data.Update(request.ExpectedRevision!.Value, json); if (updated.IsFailure) return updated.Error; }
        var rows = attributes.Select(attribute => Row(attribute, null)).ToArray();
        return new FormPersonDataResponse(userId, data.Revision, Values(json, rows, optionTexts));
    }
    private static Dictionary<string, string> ReadValues(string json) => JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException("The stored person values are invalid.");
    private static Dictionary<string, FormPersonDataValueResponse> Values(string json, IReadOnlyList<FormPersonAttributeStoredRow> attributes, IReadOnlyDictionary<Guid, string> optionTexts)
    {
        var result = new Dictionary<string, FormPersonDataValueResponse>(StringComparer.Ordinal);
        var values = ReadValues(json);
        foreach (var attribute in attributes.Where(attribute => attribute.IsActive))
            if (values.TryGetValue(attribute.Id.ToString("D"), out var value))
            {
                var text = attribute.Type == "Catalog" ? Guid.TryParse(value, out var id) ? optionTexts.GetValueOrDefault(id) ?? "" : "" : value;
                result.Add(attribute.Id.ToString("D"), new(value, text));
            }
        return result;
    }
}
