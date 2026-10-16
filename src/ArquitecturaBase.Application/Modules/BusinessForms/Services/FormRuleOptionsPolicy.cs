using System.Globalization;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal sealed class FormRuleOptionsPolicy(IFormRuleReader reader, IFormCatalogReader catalogs, IFormCatalogFilterReader filters)
{
    public async Task<FormRuleOptionsResponse?> ReadAsync(Guid actor, Guid definitionId, CancellationToken cancellationToken)
    {
        var definition = await reader.FindDefinitionAsync(definitionId, cancellationToken);
        if (definition is null) return null;
        var draft = FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson);
        var questions = draft.Fields.Concat(definition.HistoricalSchemas.OrderByDescending(version => version.Number)
            .SelectMany(version => FormDraftPolicy.Read<FormVersionSchema>(version.SchemaJson).Questions)).DistinctBy(field => field.FieldId).ToArray();
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "en" ? "en" : "es";
        var byCatalog = new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        var choices = new Dictionary<Guid,IReadOnlyList<FormRuleConditionChoiceResponse>>();
        foreach (var field in questions.Where(field => field.Type == "choice" && field.Source?.Catalog is not null))
        {
            var catalogId = field.Source!.Catalog!.Value;
            if (!byCatalog.TryGetValue(catalogId, out var options))
            {
                options = await ReadCatalogAsync(actor,catalogId,culture,cancellationToken);
                byCatalog.Add(catalogId, options);
            }
            choices.Add(field.FieldId, options);
        }
        return new FormRuleOptionsResponse(definition.Id, FormDraftPolicy.Read<Dictionary<string,string>>(definition.NameJson), definition.OriginalCulture,
            questions, await reader.ListRolesAsync(cancellationToken)) { ConditionChoices = choices };
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
        var result = new List<FormRuleConditionChoiceResponse>();
        for (var page = 1; page <= 5; page++)
        {
            var items = await catalogs.ListItemsAsync(catalogId,new() { Page = page, PageSize = 100, DisplayCulture = culture },cancellationToken);
            result.AddRange(items.Items.Select(item => new FormRuleConditionChoiceResponse(item.Id,
                item.Text.GetValueOrDefault(culture) ?? item.Text.GetValueOrDefault("es") ?? "")));
            if (!items.HasNext) break;
        }
        return result;
    }
}
