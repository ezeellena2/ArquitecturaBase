using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormConditionChoicePolicy(IFormCatalogReader catalogs,IFormCatalogFilterReader filters)
{
    public async Task<IReadOnlyDictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>> ReadAsync(Guid actor,IReadOnlyList<CanonicalFormField> questions,string culture,CancellationToken cancellationToken)
    {
        var byCatalog=new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        var choices=new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        foreach(var field in questions.Where(field=>field.Type is ("choice" or "multiChoice")&&field.Source?.Catalog is not null))
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
    // F16: the first condition (its group and place) that names an option its catalog no longer has. Only the catalogs that keep their own
    // options count (a filtered one lists what its query finds now); the choices a caller already read are reused.
    public async Task<(int Group,int Index)?> FindUnavailableAsync(Guid actor,IReadOnlyList<CanonicalFormField> questions,string culture,
        IReadOnlyList<IReadOnlyList<FormRecordCondition>> groups,IReadOnlyDictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>? known,CancellationToken cancellationToken)
    {
        var withItems=new Dictionary<Guid,bool>();
        var relevant=new List<(int Group,int Index,CanonicalFormField Field,FormRecordCondition Condition)>();
        for(var group=0;group<groups.Count;group++)
            for(var index=0;index<groups[group].Count;index++)
            {
                var condition=groups[group][index];
                if(condition.Operator is "answered" or "unanswered" or "notAsked"||condition.Values is not{Count:>0})continue;
                var field=questions.FirstOrDefault(question=>question.FieldId==condition.FieldId);
                if(field?.Type is not("choice" or "multiChoice")||field.Source?.Catalog is not{}catalogId)continue;
                if(!withItems.TryGetValue(catalogId,out var has))
                {
                    var catalog=await catalogs.FindAsync(catalogId,cancellationToken);
                    withItems[catalogId]=has=catalog is null||FormCatalogKindPolicy.HasItems(catalog.Kind);
                }
                if(has)relevant.Add((group,index,field,condition));
            }
        if(relevant.Count==0)return null;
        var choices=known??await ReadAsync(actor,[..relevant.Select(item=>item.Field).DistinctBy(field=>field.FieldId)],culture,cancellationToken);
        foreach(var item in relevant)
        {
            var ids=choices.GetValueOrDefault(item.Field.FieldId)?.Select(choice=>choice.Id).ToHashSet()??[];
            if(item.Condition.Values!.Any(value=>!Guid.TryParse(value,out var id)||!ids.Contains(id)))return (item.Group,item.Index);
        }
        return null;
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
        return items.Select(item => new FormRuleConditionChoiceResponse(item.Id, item.TextFor(culture))).ToArray();
    }
}
