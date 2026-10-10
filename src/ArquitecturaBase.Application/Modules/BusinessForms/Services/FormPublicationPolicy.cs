using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Resources;
using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormPublicationPolicy
{
    public static List<FormPublicationIssueResponse> MissingTranslations(FormDefinitionResponse definition)
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
            foreach (var culture in new[] { "es", "en" })
                if (!texts.TryGetValue(culture, out var text) || string.IsNullOrWhiteSpace(text))
                    issues.Add(new(null, fieldId, choiceId, path + "." + culture, FormPublicationErrors.MissingTranslation.Code,
                        ErrorMessages.Get(FormPublicationErrors.MissingTranslation.Code), null, culture));
        }
    }
}