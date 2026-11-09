using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormPublicationPolicy
{
    private static readonly string[] SupportedCultures = ["es", "en"];
    public static List<FormPublicationIssueResponse> ValidateFiles(FormDefinitionResponse definition, bool storageAvailable)
    {
        var fields = definition.Draft.Fields.Where(field => field.Type is "photo" or "document").ToArray();
        var issues = new List<FormPublicationIssueResponse>();
        if (fields.Length > 0 && !storageAvailable)
            Add(FormPublicationErrors.FileStorageUnavailable, null);
        if (fields.Sum(field => field.Files?.MaxFiles ?? 0) > 10)
            Add(FormPublicationErrors.TooManyFiles, 10);
        return issues;
        void Add(ArquitecturaBase.Domain.Results.Error error, int? limit) =>
            issues.Add(new(null, null, null, "draft.fields", error.Code, ErrorMessages.Get(error.Code), limit, null));
    }

    // A block of rows is published with at least one question: its first one comes right after it.
    public static List<FormPublicationIssueResponse> ValidateRows(FormDefinitionResponse definition)
    {
        var fields = definition.Draft.Fields;
        return Enumerable.Range(0, fields.Count).Where(index => fields[index].Type == "group" && (index + 1 == fields.Count || fields[index + 1].Repeated != true))
            .Select(index => new FormPublicationIssueResponse(null, fields[index].FieldId, null, $"draft.fields[{index}]",
                FormPublicationErrors.GroupEmpty.Code, ErrorMessages.Get(FormPublicationErrors.GroupEmpty.Code), null, null)).ToList();
    }

    // A section title is published with at least one question below it, before the next title or the end (CW15-B).
    public static List<FormPublicationIssueResponse> ValidateSections(FormDefinitionResponse definition)
    {
        var sections = definition.Draft.Sections ?? [];
        var count = definition.Draft.Fields.Count;
        return Enumerable.Range(0, sections.Count).Where(index => sections[index].Start >= (index + 1 < sections.Count ? sections[index + 1].Start : count))
            .Select(index => new FormPublicationIssueResponse(null, null, null, $"draft.sections[{index}]",
                FormPublicationErrors.SectionEmpty.Code, ErrorMessages.Get(FormPublicationErrors.SectionEmpty.Code), null, null)).ToList();
    }

    // A calculation (CA1-A) uses earlier number questions within its reach: multiply and percent take those of the same item when
    // the calculation is in the block and those outside it otherwise; a sum goes outside the block and adds a question of the block
    // over all the items. Earlier operands also rule out cycles.
    public static List<FormPublicationIssueResponse> ValidateCalculations(FormDefinitionResponse definition)
    {
        var fields = definition.Draft.Fields;
        var issues = new List<FormPublicationIssueResponse>();
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].Calculation is not { } calculation) continue;
            var inBlock = fields[index].Repeated == true;
            var earlier = fields.Take(index).ToDictionary(field => field.FieldId);
            // The rate of a percentage may also be a choice from a catalog: its option's value is the rate (the guard checks the type).
            var valid = (calculation.Operation != FormCalculation.Sum || !inBlock) && calculation.Operands.Select((operand, position) =>
                earlier.TryGetValue(operand, out var field)
                && (field.Type == "decimal" || (calculation.Operation == FormCalculation.Percent && position == 1 && field.Type == "choice" && field.Source?.Catalog is not null))
                && (calculation.Operation == FormCalculation.Sum || (field.Repeated == true) == inBlock)).All(ok => ok);
            if (!valid)
                issues.Add(new(null, fields[index].FieldId, null, $"draft.fields[{index}].calculation", FormPublicationErrors.CalculationInvalid.Code,
                    ErrorMessages.Get(FormPublicationErrors.CalculationInvalid.Code), null, null));
        }
        return issues;
    }

    // CO1-A: a condition names an earlier question outside the block, of choose one (own or from a catalog) or yes or no, and
    // answers it has: options of its own, identities of catalog options, or "true"/"false".
    public static List<FormPublicationIssueResponse> ValidateConditions(FormDefinitionResponse definition)
    {
        var fields = definition.Draft.Fields;
        var issues = new List<FormPublicationIssueResponse>();
        for (var index = 0; index < fields.Count; index++)
        {
            if (fields[index].ShowWhen is not { } showWhen) continue;
            var question = fields.Take(index).FirstOrDefault(field => field.FieldId == showWhen.QuestionId);
            var valid = fields[index].Type != "group" && fields[index].Repeated != true && question is { Repeated: not true } && question.Type switch
            {
                "boolean" => showWhen.Values.All(value => value is "true" or "false"),
                "choice" when question.Source?.Catalog is not null => showWhen.Values.All(value => Guid.TryParseExact(value, "D", out _)),
                "choice" => showWhen.Values.All(value => question.Options?.Any(option => option.ChoiceId.ToString("D") == value) == true),
                _ => false,
            };
            if (!valid)
                issues.Add(new(null, fields[index].FieldId, null, $"draft.fields[{index}].showWhen", FormPublicationErrors.ConditionInvalid.Code,
                    ErrorMessages.Get(FormPublicationErrors.ConditionInvalid.Code), null, null));
        }
        return issues;
    }

    // A minimum above the rows a channel loads blocks publishing with that channel (RG13-A): no response would reach it.
    public static List<FormPublicationIssueResponse> RowIssues(FormDefinitionResponse definition)
    {
        var fields = definition.Draft.Fields;
        return Enumerable.Range(0, fields.Count).Where(index => fields[index] is { Type: "group", Rows.MinRows: > StructuredFormLimits.MaxRows })
            .Select(index => new FormPublicationIssueResponse(null, fields[index].FieldId, null, $"draft.fields[{index}].rows",
                FormPublicationErrors.MinRowsOverChannelLimit.Code, ErrorMessages.Get(FormPublicationErrors.MinRowsOverChannelLimit.Code),
                StructuredFormLimits.MaxRows, null)).ToList();
    }

    // Above the rows a channel loads, publishing warns without blocking: the app keeps loading up to the maximum (RG3-A).
    public static List<FormPublicationWarningResponse> RowWarnings(FormDefinitionResponse definition)
    {
        var fields = definition.Draft.Fields;
        return Enumerable.Range(0, fields.Count).Where(index => fields[index] is { Type: "group", Rows.MaxRows: > StructuredFormLimits.MaxRows })
            .Select(index => new FormPublicationWarningResponse(fields[index].FieldId, $"draft.fields[{index}].rows",
                FormPublicationErrors.RowsOverChannelLimit.Code, ErrorMessages.Get(FormPublicationErrors.RowsOverChannelLimit.Code),
                StructuredFormLimits.MaxRows, fields[index].Rows!.MaxRows)).ToList();
    }

    // A published question that entered the block comes back with another identity (RG6-A): each new repeated question pairs with
    // a removed one of the same type, catalog and original name, so publishing says «Pasan al bloque» instead of removed and added.
    public static IReadOnlyList<(Guid Added, Guid Removed)> MovedToRows(IReadOnlyList<CanonicalFormField> removed,
        IReadOnlyList<CanonicalFormField> added, string culture)
    {
        var free = removed.Where(field => field.Type != "group" && field.Repeated != true).ToList();
        var pairs = new List<(Guid Added, Guid Removed)>();
        foreach (var field in added.Where(field => field.Repeated == true))
        {
            var match = free.FirstOrDefault(old => old.Type == field.Type && old.Source?.Catalog == field.Source?.Catalog
                && string.Equals(NameOf(old, culture), NameOf(field, culture), StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            free.Remove(match);
            pairs.Add((field.FieldId, match.FieldId));
        }
        return pairs;
        static string? NameOf(CanonicalFormField field, string culture) => field.Label.TryGetValue(culture, out var name) ? name.Normalize().Trim() : null;
    }

    public static IReadOnlyList<FormPublicationCultureResponse> CheckCultures(FormDefinitionResponse definition) =>
        SupportedCultures.Select(culture =>
        {
            var missing = MissingTranslations(definition, culture);
            return new FormPublicationCultureResponse(culture, culture == definition.Draft.Culture, missing.Count == 0, missing);
        }).ToArray();

    public static List<FormPublicationIssueResponse> MissingTranslations(FormDefinitionResponse definition) =>
        MissingTranslations(definition, definition.Draft.Culture);

    private static List<FormPublicationIssueResponse> MissingTranslations(FormDefinitionResponse definition, string culture)
    {
        var issues = new List<FormPublicationIssueResponse>();
        Check(definition.Name, "name");
        Check(definition.Draft.SubmitLabel, "draft.submitLabel");
        for (var index = 0; index < definition.Draft.Fields.Count; index++)
        {
            var field = definition.Draft.Fields[index];
            var path = $"draft.fields[{index}]";
            Check(field.Label, path + ".label", field.FieldId);
            if (field.Help is not null && field.Help.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                Check(field.Help, path + ".help", field.FieldId);
            for (var optionIndex = 0; optionIndex < (field.Options?.Count ?? 0); optionIndex++)
            {
                var option = field.Options![optionIndex];
                Check(option.Label, $"{path}.options[{optionIndex}].label", field.FieldId, option.ChoiceId);
            }
        }
        for (var index = 0; index < (definition.Draft.Sections?.Count ?? 0); index++)
            Check(definition.Draft.Sections![index].Title, $"draft.sections[{index}].title");
        return issues;
        void Check(IReadOnlyDictionary<string, string> texts, string path, Guid? fieldId = null, Guid? choiceId = null)
        {
            if (!texts.TryGetValue(culture, out var text) || string.IsNullOrWhiteSpace(text))
                issues.Add(new(null, fieldId, choiceId, path + "." + culture, FormPublicationErrors.MissingTranslation.Code,
                    ErrorMessages.Get(FormPublicationErrors.MissingTranslation.Code), null, culture));
        }
    }
}
