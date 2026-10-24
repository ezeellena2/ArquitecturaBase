using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

// The most options one person can be offered by a question with valid filters: what the fixed filters leave, grouped by the
// person filters. The label names the person values of that largest group; with only fixed filters everyone sees the same ones.
internal sealed class FormCatalogRecipientMaximumPolicy(IFormCatalogReader catalogs)
{
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
