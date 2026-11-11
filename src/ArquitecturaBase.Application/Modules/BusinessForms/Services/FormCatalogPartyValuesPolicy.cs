using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// What a registry knows of a party, put in the active fields of a catalog by their keys: a value without its field is dropped, and a
/// catalog field gets the active option of its source with the code the registry gave, or nothing (one read per source). «Completar»
/// shows them to the person; the party an issuer completed keeps the ones that fit their field.
/// </summary>
internal sealed class FormCatalogPartyValuesPolicy(IFormCatalogItemCodeReader codes)
{
    public async Task<IReadOnlyList<(BusinessFormCatalogField Field, string Value)>> MapAsync(IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyDictionary<string, string>? values, CancellationToken cancellationToken)
    {
        if (values is null) return [];
        var known = fields.Where(field => field.IsActive && values.ContainsKey(field.Key)).ToArray();
        var options = new Dictionary<Guid, IReadOnlyDictionary<string, Guid>>();
        foreach (var source in known.Where(field => field.Type == "catalog" && field.SourceCatalogId is not null).GroupBy(field => field.SourceCatalogId!.Value))
            options[source.Key] = await codes.ListActiveAsync(source.Key, source.Select(field => values[field.Key]).Distinct(StringComparer.Ordinal).ToArray(),
                cancellationToken);
        var mapped = new List<(BusinessFormCatalogField, string)>();
        foreach (var field in known)
        {
            var value = values[field.Key];
            if (field.Type != "catalog") mapped.Add((field, value));
            else if (field.SourceCatalogId is { } source && options[source].TryGetValue(value, out var optionId)) mapped.Add((field, optionId.ToString("D")));
        }
        return mapped;
    }
}
