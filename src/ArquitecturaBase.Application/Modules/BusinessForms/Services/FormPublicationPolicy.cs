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
        return issues;
        void Check(IReadOnlyDictionary<string, string> texts, string path, Guid? fieldId = null, Guid? choiceId = null)
        {
            if (!texts.TryGetValue(culture, out var text) || string.IsNullOrWhiteSpace(text))
                issues.Add(new(null, fieldId, choiceId, path + "." + culture, FormPublicationErrors.MissingTranslation.Code,
                    ErrorMessages.Get(FormPublicationErrors.MissingTranslation.Code), null, culture));
        }
    }
}
