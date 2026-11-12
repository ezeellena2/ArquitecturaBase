using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// A form as a documents read shows it (decision 8): its name and «Cómo reconocerlo» in the conversation's language (or its original),
/// and the questions a reading fills, as the assistant's prefill takes them: short and long texts with their limit, numbers, dates, yes
/// or no and choosing one, of its own options or of a catalog (by name: the server fills it). Never the questions of a block of rows,
/// one that chooses first by a field, a calculation or one with a condition, nor photos, documents or locations.
/// </summary>
internal static class FormDocumentFormPolicy
{
    public static FormDocumentReading? Reading(FormDocumentFormRow row) =>
        row.DocumentReadingJson is { } json ? FormDraftPolicy.Read<FormDocumentReading>(json) : null;

    public static IReadOnlyList<CanonicalFormField> ReadQuestions(FormVersionSchema schema) => schema.Questions
        .Where(field => FormDocumentCapabilityCodec.QuestionTypes.Contains(field.Type, StringComparer.Ordinal) && field.Repeated != true
            && field.Source?.GroupBy is null && field.Calculation is null && field.ShowWhen is null
            && (field.Type is not ("shortText" or "longText") || field.MaxLength is >= 1 and <= 10000))
        .Take(FormDocumentCapabilityCodec.MaxQuestions).ToArray();

    public static FormDocumentCandidate Candidate(FormDocumentFormRow row, FormVersionSchema schema, string culture,
        IReadOnlyList<FormDocumentCatalogNameRow> catalogs)
    {
        var original = row.Culture;
        var reading = Reading(row);
        var questions = ReadQuestions(schema).Select(field => new FormDocumentQuestion(field.FieldId, field.Type,
            FormAssistantCandidatePolicy.Text(field.Label, culture, original), field.Required,
            field.Type is "shortText" or "longText" ? field.MaxLength : null,
            field.Source?.Catalog is null ? field.Options?.Select(option => new FormAssistantChoice(option.ChoiceId,
                FormAssistantCandidatePolicy.Text(option.Label, culture, original))).ToArray() ?? [] : [],
            field.Source?.Catalog is { } catalogId ? CatalogName(catalogs, catalogId, culture) : null)).ToArray();
        return new(row.DefinitionId, row.VersionId, row.DefinitionRevision, Name(row, culture),
            reading is { Enabled: true } && !string.IsNullOrWhiteSpace(reading.Recognition) ? reading.Recognition.Trim() : null, questions);
    }

    public static string Name(FormDocumentFormRow row, string culture) =>
        FormAssistantCandidatePolicy.Text(FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson), culture, row.Culture);

    // A catalog that cannot be named (deleted, or without a name in either language) is still a catalog: its question stays empty.
    private static string CatalogName(IReadOnlyList<FormDocumentCatalogNameRow> catalogs, Guid catalogId, string culture)
    {
        var catalog = catalogs.FirstOrDefault(value => value.CatalogId == catalogId);
        var names = catalog is null ? null : FormDraftPolicy.Read<Dictionary<string, string>>(catalog.NameJson);
        return names is null ? "—" : names.TryGetValue(culture, out var name) && !string.IsNullOrWhiteSpace(name) ? name
            : names.TryGetValue(catalog!.OriginalCulture, out name) && !string.IsNullOrWhiteSpace(name) ? name : names.Values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";
    }
}
