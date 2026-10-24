using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The most options one person can be offered by a question with valid filters: what the fixed filters leave, grouped by the
// person filters. The label names the person values of that largest group; with only fixed filters everyone sees the same ones.
internal sealed class FormCatalogRecipientMaximumPolicy(IFormCatalogReader catalogs)
{
    // A fixed value on a catalog field must be an active option of the field's source; otherwise publishing blocks the filter, so
    // neither the warning nor the editor give it a maximum. The sources read are kept in the caller's cache.
    public async Task<Error?> CheckFixedValuesAsync(IReadOnlyList<BusinessFormCatalogField> fields, IReadOnlyList<FormCatalogMatch>? matches,
        Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> sourceItems, CancellationToken ct)
    {
        foreach (var match in matches ?? [])
        {
            if (match.Value is null || fields.Single(field => field.Id == match.FieldId) is not { Type: "catalog", SourceCatalogId: { } sourceId }) continue;
            if (!sourceItems.TryGetValue(sourceId, out var options)) sourceItems.Add(sourceId, options = await catalogs.ListAllItemsAsync(sourceId, ct));
            var itemId = Guid.Parse(match.Value);
            if (!options.Any(item => item.IsActive && item.Id == itemId)) return FormPublicationErrors.MatchValueUnavailable;
        }
        return null;
    }

    public async Task<(int Maximum, string? Label)> FindAsync(Guid catalogId, IReadOnlyList<BusinessFormCatalogField> fields,
        IReadOnlyList<FormCatalogMatch> matches, string culture, CancellationToken ct)
    {
        var personFields = matches.Where(match => match.PersonAttributeId is not null).Select(match => match.FieldId).ToArray();
        var fixedContainment = FormCatalogMatchPolicy.FixedContainment(matches, fields).Value;
        var maximum = await catalogs.FindMaximumOfferSizeAsync(catalogId, fixedContainment, personFields, ct);
        if (maximum.Maximum == 0 || personFields.Length == 0) return (maximum.Maximum, null);
        var choices = new Dictionary<Guid, FormCatalogStoredItemRow>();
        foreach (var sourceId in fields.Where(field => personFields.Contains(field.Id) && field.SourceCatalogId is not null)
            .Select(field => field.SourceCatalogId!.Value).Distinct())
            foreach (var item in await catalogs.ListAllItemsAsync(sourceId, ct)) choices.Add(item.Id, item);
        // The group row only carries the person field values: it has no value of its own, so the catalog value type does not matter.
        var labels = FormCatalogFieldPresentation.Values(new(Guid.Empty, catalogId, "", null, null, null, null, 0, true, FieldValuesJson: maximum.ValuesJson),
            fields, choices, culture, "Text");
        var label = string.Join(" · ", personFields.Order().Select(id => labels.GetValueOrDefault(id.ToString("D"))).Where(value => !string.IsNullOrEmpty(value)));
        return (maximum.Maximum, label.Length == 0 ? null : label);
    }
}
