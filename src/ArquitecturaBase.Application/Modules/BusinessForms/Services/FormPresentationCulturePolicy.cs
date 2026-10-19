using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal sealed class FormPresentationCulturePolicy(IFormRuleReader rules, IFormPdfDesignReader pdfDesigns)
{
    public async Task<StructuredFormPreparationResponse> PrepareAsync(Guid definitionId, string originalCulture,
        string requestedCulture, string nameJson, string submitLabelJson, FormVersionSchema schema,
        StructuredFormPreparationResponse prepared, CancellationToken ct)
    {
        var ruleSet = await rules.FindCurrentAsync(definitionId, ct);
        var revisionRules = ruleSet is null ? [] : FormDraftPolicy.Read<FormRule[]>(ruleSet.RulesJson);
        var (complete, _) = await CompleteAsync(definitionId, originalCulture, nameJson, schema, revisionRules, ct);
        var submit = FormDraftPolicy.Read<Dictionary<string, string>>(submitLabelJson);
        var culture = complete.Contains(requestedCulture) && Has(submit, requestedCulture)
            && prepared.Catalogs.All(catalog => CatalogComplete(catalog, requestedCulture))
            ? requestedCulture : originalCulture;
        var unavailable = prepared.Catalogs.Where(catalog => !CatalogComplete(catalog, culture)).ToArray();
        if (unavailable.Length > 0)
            return new([], unavailable.Select(catalog => new StructuredFormPreparationIssue(catalog.FieldId,
                schema.Questions.Single(question => question.FieldId == catalog.FieldId).Label[originalCulture],
                FormRecordErrors.CatalogUnavailable.Code)).ToArray()) { Culture = originalCulture };
        var catalogs = prepared.Catalogs.Select(catalog => catalog with
        {
            Label = schema.Questions.Single(question => question.FieldId == catalog.FieldId).Label[culture],
            CatalogName = catalog.CatalogNames![culture],
            Options = catalog.Options.Select(option => Localize(option, culture)).ToArray(),
        }).ToArray();
        return prepared with { Culture = culture, Catalogs = catalogs };
    }

    public async Task<IReadOnlyList<string>> CompleteForRecordAsync(FormActionRecordSnapshot snapshot,
        IReadOnlyList<FormRule> revisionRules, FormVersionSchema ruleQuestions, CancellationToken ct)
    {
        var captured = snapshot.Response.Record;
        var schema = FormDraftPolicy.Read<FormVersionSchema>(captured.SchemaJson);
        var (complete, design) = await CompleteAsync(captured.DefinitionId, snapshot.OriginalCulture, captured.NameJson, schema, revisionRules, ct);
        var used = FormActionMessagePolicy.UsedFields(revisionRules, ruleQuestions);
        if (design is not null)
            used.UnionWith(schema.Questions.Where(question => design.QuestionIds.Contains(question.FieldId)).Select(question => question.FieldId));
        return complete.Intersect(FormRecordReferencePolicy.CompleteCultures(snapshot, used)).ToArray();
    }

    private async Task<(IReadOnlyList<string> Cultures, FormPdfDesign? Pdf)> CompleteAsync(Guid definitionId, string originalCulture, string nameJson,
        FormVersionSchema schema, IReadOnlyList<FormRule> revisionRules, CancellationToken ct)
    {
        var name = FormDraftPolicy.Read<Dictionary<string, string>>(nameJson);
        var complete = FormActionMessagePolicy.CompleteCultures(revisionRules, originalCulture)
            .Where(culture => Has(name, culture) && schema.Questions.All(question => Has(question.Label, culture)
                && (question.Help is null || !Has(question.Help, originalCulture) || Has(question.Help, culture))
                && (question.Options?.All(option => Has(option.Label, culture)) ?? true))).ToArray();
        if (!revisionRules.SelectMany(rule => rule.Actions).Any(action => action.Kind == "GeneratePdf")) return (complete, null);
        var current = await pdfDesigns.FindCurrentAsync(definitionId, ct);
        var design = FormPdfDesignPolicy.Resolve(current, nameJson, schema);
        return (complete.Intersect(FormPdfDesignPolicy.CompleteCultures(design, originalCulture, schema, nameJson)).ToArray(), design);
    }

    private static bool CatalogComplete(StructuredFormCatalogOffer catalog, string culture) =>
        catalog.CatalogNames is { } names && Has(names, culture) && catalog.Options.All(option =>
            option.Translations is { } translations &&
            (translations.TryGetValue(culture, out var text) && !string.IsNullOrWhiteSpace(text.Text)
                || culture == "en" && translations.TryGetValue("es", out var original) && !string.IsNullOrWhiteSpace(original.Text)));
    private static StructuredFormCatalogOption Localize(StructuredFormCatalogOption option, string culture)
    {
        var translations = option.Translations!;
        var text = translations.GetValueOrDefault(culture);
        var original = translations.GetValueOrDefault("es") ?? text!;
        return option with
        {
            Text = !string.IsNullOrWhiteSpace(text?.Text) ? text.Text : original.Text,
            Detail = !string.IsNullOrWhiteSpace(text?.Detail) ? text.Detail : original.Detail
        };
    }
    private static bool Has(IReadOnlyDictionary<string, string> texts, string culture) =>
        texts.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value);
}
