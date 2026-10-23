using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormRecordPresentationPolicy
{
    public static StructuredRecordReadReply Project(FormRecordStoredRow row, string culture, FormVersionSchema schema, bool includeCatalogFields = true)
    {
        var name = FormDraftPolicy.Read<Dictionary<string, string>>(row.NameJson);
        if (schema.Format != 1)
            throw new InvalidOperationException("The sealed form record presentation is invalid.");
        var localizedName = Text(name, culture);

        using var document = JsonDocument.Parse(row.ValuesJson);
        var saved = new Dictionary<Guid, JsonElement>();
        foreach (var value in document.RootElement.EnumerateArray())
        {
            if (!value.TryGetProperty("fieldId", out var fieldIdElement)
                || !fieldIdElement.TryGetGuid(out var fieldId)
                || !value.TryGetProperty("value", out var answer)
                || !saved.TryAdd(fieldId, answer))
                throw new InvalidOperationException("The captured form record values are invalid.");
        }

        var values = schema.Questions.Select(question => new StructuredRecordReadValue(question.FieldId,
            question.Key, question.Type, Text(question.Label, culture), question.Type is "photo" or "document"
                ? (row.Files ?? []).Where(file => file.FieldId == question.FieldId).OrderBy(file => file.Position)
                    .Select(file => new StructuredRecordFileValue(file.FileId, file.Name, file.ContentType, file.Size, file.HasThumbnail)).ToArray()
                : saved.TryGetValue(question.FieldId, out var answer)
                ? Decode(question, answer, culture, row.References ?? [], includeCatalogFields) : null)).ToArray();
        var number = row.Prefix + "-" + row.Number.ToString("D6", CultureInfo.InvariantCulture);
        return new(row.SourceReceiptId, row.RecordId, number, row.DefinitionId, row.VersionId,
            row.VersionNumber, localizedName, values);
    }

    internal static object Decode(CanonicalFormField question, JsonElement answer, string culture, IReadOnlyList<FormRecordReferenceRow> references, bool includeCatalogFields = true) => question.Type switch
    {
        "shortText" or "longText" => answer.GetString()!,
        "decimal" => answer.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture),
        "date" => answer.GetString()!,
        "boolean" => answer.GetBoolean(),
        "choice" => DecodeSelection(question, answer, culture, references, includeCatalogFields),
        "multiChoice" => answer.EnumerateArray().Select(value => DecodeSelection(question, value, culture, references, includeCatalogFields)).ToArray(),
        _ => throw new InvalidOperationException("The captured form contains an unsupported field type."),
    };

    private static StructuredRecordChoiceValue DecodeSelection(CanonicalFormField question, JsonElement answer, string culture,
        IReadOnlyList<FormRecordReferenceRow> references, bool includeCatalogFields) => question.Source?.Catalog is not null
            ? DecodeReference(question, answer, references, culture, includeCatalogFields) : DecodeChoice(question, answer, culture);

    private static StructuredRecordChoiceValue DecodeReference(CanonicalFormField question, JsonElement answer,
        IReadOnlyList<FormRecordReferenceRow> references, string culture, bool includeCatalogFields)
    {
        var reference = references.Single(value => value.FieldId == question.FieldId
            && value.CatalogId == question.Source!.Catalog && value.ItemId == answer.GetGuid());
        var text = FormRecordReferencePolicy.Text(reference, culture);
        return new(reference.ItemId.ToString("D"), text.Text, reference.CatalogId, text.CatalogName, text.Detail, reference.Value, reference.ValueType,
            includeCatalogFields && reference.FieldValuesJson is { } json ? CapturedFields(json, culture) : null);
    }

    private static StructuredRecordCatalogFieldValue[] CapturedFields(string json, string culture) =>
        FormDraftPolicy.Read<FormCatalogCapturedField[]>(json).Select(field => new StructuredRecordCatalogFieldValue(
            field.Id, Text(field.Name, culture), field.Type, field.Role, FormCatalogFieldPolicy.Input(field.Value)!,
            field.Text is null ? null : Text(field.Text, culture))).ToArray();

    private static StructuredRecordChoiceValue DecodeChoice(CanonicalFormField question, JsonElement answer, string culture)
    {
        if (!Guid.TryParse(answer.GetString(), out var choiceId)
            || question.Options?.SingleOrDefault(option => option.ChoiceId == choiceId) is not { } choice)
            throw new InvalidOperationException("The captured choice does not belong to its sealed version.");
        return new(choiceId.ToString("D"), Text(choice.Label, culture));
    }

    internal static string Text(IReadOnlyDictionary<string, string> translations, string culture) =>
        translations.TryGetValue(culture, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : translations.TryGetValue(culture == "en" ? "es" : "en", out value) && !string.IsNullOrWhiteSpace(value)
                ? value : throw new InvalidOperationException("The sealed form is missing its original text.");
}
