using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormPdfDesignPolicy
{
    public static Result<FormPdfDesign> Normalize(FormPdfDesign? design, string originalCulture, IReadOnlyCollection<Guid> knownQuestions)
    {
        if (design is null) return Invalid("design");
        var name = Text(design.Name, originalCulture, 160, true);
        if (name is null) return Invalid("design.name." + originalCulture);
        var footer = Text(design.Footer, originalCulture, 1000, false);
        if (footer is null) return Invalid("design.footer." + originalCulture);
        if (design.QuestionIds is null || design.QuestionIds.Count > knownQuestions.Count
            || design.QuestionIds.Distinct().Count() != design.QuestionIds.Count || design.QuestionIds.Any(id => !knownQuestions.Contains(id)))
            return Invalid("design.questionIds");
        return new FormPdfDesign(name, design.QuestionIds.ToArray(), design.IncludeApproval, footer.Count == 0 ? null : footer, design.IncludePhotos);
    }
    public static IReadOnlyList<string> CompleteCultures(FormPdfDesign design, string originalCulture, FormVersionSchema schema, string formNameJson) => new[] { originalCulture, originalCulture == "es" ? "en" : "es" }
        .Where(culture => design.Name.TryGetValue(culture, out var name) && !string.IsNullOrWhiteSpace(name)
            && (schema is null || schema.Questions.Where(question => design.QuestionIds.Contains(question.FieldId)).All(question => !string.IsNullOrWhiteSpace(question.Label.GetValueOrDefault(culture))
                && (question.Options ?? []).All(option => !string.IsNullOrWhiteSpace(option.Label.GetValueOrDefault(culture)))))
            && (design.Footer is null || design.Footer.Count == 0 || design.Footer.TryGetValue(culture, out var footer) && !string.IsNullOrWhiteSpace(footer)))
        .ToArray();
    public static FormPdfDesign Resolve(FormPdfDesignStoredRow? current, FormRecordStoredRow record, FormVersionSchema schema) =>
        current is null
            ? new(FormDraftPolicy.Read<Dictionary<string,string>>(record.NameJson),
                schema.Questions.Select(question => question.FieldId).ToArray(), true, null, true)
            : FormDraftPolicy.Read<FormPdfDesign>(current.DesignJson);
    public static FormPdfDesign Default(FormRuleDefinitionRow definition) => new(FormDraftPolicy.Read<Dictionary<string,string>>(definition.NameJson),
        FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson).Fields.Select(field => field.FieldId).ToArray(), true, null, true);
    public static IReadOnlyList<Guid> KnownQuestions(FormRuleDefinitionRow definition) => FormDraftPolicy.Read<FormQuestionSchema>(definition.SchemaJson).Fields.Select(field => field.FieldId)
        .Concat(definition.HistoricalSchemas.SelectMany(row => FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson).Questions.Select(field => field.FieldId))).Distinct().ToArray();
    private static Dictionary<string,string>? Text(IReadOnlyDictionary<string,string>? text, string originalCulture, int maxLength, bool required)
    {
        if (text?.Keys.Any(culture => culture is not ("es" or "en")) == true) return null;
        var result = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var (culture, value) in text ?? new Dictionary<string,string>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var normalized = value.Trim();
            if (normalized.Length > maxLength || !StorableText.IsStorable(normalized)) return null;
            result.Add(culture, normalized);
        }
        return (required || result.Count > 0) && !result.ContainsKey(originalCulture) ? null : result;
    }
    private static ValidationError Invalid(string path) => FieldErrors.On(FormPdfErrors.ContentInvalid, path);
}
