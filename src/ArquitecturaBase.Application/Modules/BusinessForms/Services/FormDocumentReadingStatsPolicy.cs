using System.Globalization;
using ArquitecturaBase.Application.Common.Formatting;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Resources;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// «Cómo viene leyendo» (DOC1) beyond the counts: the question corrected most often, the questions whose values mark a repeated
/// document (the standard's <c>DuplicateKeys</c> through the bound map sealed in the copy) and a correction as display text, the way
/// a message shows a value: a number in the reader's language, a date with numbers only, yes or no, and an option by its text.
/// </summary>
internal static class FormDocumentReadingStatsPolicy
{
    // Ties go to the lowest id, so the answer does not change between two reads of the same data. Null without corrections.
    public static FormDocumentReadingCorrectedResponse? MostCorrected(IReadOnlyList<FormDocumentReadingQuestionResponse> questions) =>
        questions.Where(question => question.Read > question.Unchanged).OrderByDescending(question => question.Read - question.Unchanged)
            .ThenBy(question => question.QuestionId).Select(question => new FormDocumentReadingCorrectedResponse(question.QuestionId, question.Read - question.Unchanged))
            .FirstOrDefault();

    // Only a copy of an installed standard of the same version that reads documents has them; an own form has none.
    public static IReadOnlyList<Guid> DuplicateQuestionIds(FormDocumentReadingDefinitionRow definition, IEnumerable<StructuredFormStandard> standards)
    {
        if (definition.StandardKey is not { } key || definition.StandardBindingsJson is not { } bindingsJson) return [];
        if (standards.FirstOrDefault(standard => standard.Key == key && standard.Version == definition.StandardVersion)?.Reading is not { } reading) return [];
        var bound = FormDraftPolicy.Read<Dictionary<string, Guid>>(bindingsJson);
        return reading.DuplicateKeys.Where(bound.ContainsKey).Select(datum => bound[datum]).ToArray();
    }

    public static FormDocumentReadingExampleResponse Example(FormDocumentReadingCorrectionRow row, string prefix, CanonicalFormField? question, string culture) =>
        new(row.RecordId, prefix + "-" + row.Number.ToString("D6", CultureInfo.InvariantCulture), row.SubmittedAtUtc, row.FieldId,
            Text(question, row.Read, row.ReadText, culture), Text(question, row.Confirmed, row.ConfirmedText, culture));

    // The canonical value as the reader sees it; empty when there is none. A catalog option shows the text its capture kept.
    public static string Text(CanonicalFormField? question, string? value, string? catalogText, string culture)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (question is { Type: "choice", Source: not null }) return catalogText ?? value;
        return question?.Type switch
        {
            "decimal" when decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number) =>
                DisplayNumbers.Number(number, culture),
            "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) =>
                date.ToString(culture == "en" ? "MM/dd/yyyy" : "dd/MM/yyyy", CultureInfo.InvariantCulture),
            "boolean" when bool.TryParse(value, out var boolean) => FormResponseTexts.Get(boolean ? "true" : "false", culture),
            "choice" when Guid.TryParse(value, out var choice) && question.Options?.FirstOrDefault(option => option.ChoiceId == choice) is { } option =>
                FormRecordPresentationPolicy.Text(option.Label, culture),
            _ => value,
        };
    }
}
