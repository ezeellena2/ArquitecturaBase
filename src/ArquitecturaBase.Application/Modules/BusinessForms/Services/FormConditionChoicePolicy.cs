using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormConditionChoicePolicy(IFormCatalogReader catalogs,IFormCatalogFilterReader filters)
{
    public async Task<IReadOnlyDictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>> ReadAsync(Guid actor,IReadOnlyList<CanonicalFormField> questions,string culture,CancellationToken cancellationToken)
    {
        var byCatalog=new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        var choices=new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        foreach(var field in questions.Where(field=>field.Type=="choice"&&field.Source?.Catalog is not null))
        {
            var catalogId=field.Source!.Catalog!.Value;
            if(!byCatalog.TryGetValue(catalogId,out var options))
            {
                options=await ReadCatalogAsync(actor,catalogId,culture,cancellationToken);
                byCatalog.Add(catalogId,options);
            }
            choices.Add(field.FieldId,options);
        }
        return choices;
    }
    private async Task<IReadOnlyList<FormRuleConditionChoiceResponse>> ReadCatalogAsync(Guid actor, Guid catalogId, string culture, CancellationToken cancellationToken)
    {
        var catalog = await catalogs.FindAsync(catalogId,cancellationToken);
        if (catalog is null) return [];
        if (catalog.Kind == "Filters")
        {
            if (catalog.FiltersJson is null) return [];
            var definition = FormDraftPolicy.Read<FormCatalogFilterDefinition>(catalog.FiltersJson);
            var preview = await filters.FindPreviewAsync(actor, definition,cancellationToken);
            return preview.Items.Select(item => new FormRuleConditionChoiceResponse(item.Id,
                definition.TextColumn == "active" ? FormCatalogOfferPolicy.LocalizeBoolean(item.Text,culture) : item.Text)).ToArray();
        }
        var items = await catalogs.ListAllItemsAsync(catalogId, cancellationToken);
        return items.Select(item => new FormRuleConditionChoiceResponse(item.Id,
            culture == "en" ? item.TextEn ?? item.TextEs : item.TextEs)).ToArray();
    }
}
