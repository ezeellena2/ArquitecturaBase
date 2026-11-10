using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The bound data of a response for its issuer, by the datum keys its standard gave (BI1-A): what the response left empty does not
/// come, and the block of rows brings its items with their own bound questions. Texts go in the culture of the form.
/// </summary>
internal static class FormIssueDataPolicy
{
    public static IReadOnlyList<StructuredIssueDatum> Read(FormRecordStoredRow row, IReadOnlyDictionary<string, Guid> bound, string culture)
    {
        var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        var keys = bound.ToDictionary(pair => pair.Value, pair => pair.Key);
        var references = row.References ?? [];
        using var document = JsonDocument.Parse(row.ValuesJson);
        var answers = Answers(document.RootElement);
        var data = new List<StructuredIssueDatum>();
        foreach (var question in schema.Questions.Where(question => question.Repeated != true && keys.ContainsKey(question.FieldId)))
        {
            if (!answers.TryGetValue(question.FieldId, out var answer)) continue;
            if (question.Type != "group")
            {
                if (Datum(keys[question.FieldId], question, answer, references, culture) is { } datum) data.Add(datum);
                continue;
            }
            var block = schema.Questions.SkipWhile(value => value.FieldId != question.FieldId).Skip(1)
                .TakeWhile(value => value.Repeated == true).Where(value => keys.ContainsKey(value.FieldId)).ToArray();
            var items = answer.EnumerateArray().Select((item, position) =>
            {
                var itemAnswers = Answers(item);
                var own = references.Where(reference => reference.Row == position).ToArray();
                return (IReadOnlyList<StructuredIssueDatum>)block.Where(value => itemAnswers.ContainsKey(value.FieldId))
                    .Select(value => Datum(keys[value.FieldId], value, itemAnswers[value.FieldId], own, culture))
                    .OfType<StructuredIssueDatum>().ToArray();
            }).ToArray();
            data.Add(new StructuredIssueDatum(keys[question.FieldId], null, Items: items));
        }
        return data;
    }

    private static Dictionary<Guid, JsonElement> Answers(JsonElement values) =>
        values.EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value").Clone());

    private static StructuredIssueDatum? Datum(string key, CanonicalFormField question, JsonElement answer,
        IReadOnlyList<FormRecordReferenceRow> references, string culture) => question.Type switch
    {
        "shortText" or "longText" or "date" => new(key, answer.GetString()),
        "decimal" => new(key, answer.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture)),
        "boolean" => new(key, answer.GetBoolean() ? "true" : "false"),
        "choice" => question.Source?.Catalog is { } catalogId ? CatalogChoice(key, question, catalogId, answer, references, culture) : OwnChoice(key, question, answer, culture),
        _ => null,
    };

    private static StructuredIssueDatum CatalogChoice(string key, CanonicalFormField question, Guid catalogId, JsonElement answer,
        IReadOnlyList<FormRecordReferenceRow> references, string culture)
    {
        var reference = references.Single(value => value.FieldId == question.FieldId && value.CatalogId == catalogId && value.ItemId == answer.GetGuid());
        var fields = reference.FieldValuesJson is { } json
            ? FormDraftPolicy.Read<FormCatalogCapturedField[]>(json).Select(field => (field.Key, Value: FormCatalogFieldPolicy.Input(field.Value)))
                .Where(field => field.Value is not null).ToDictionary(field => field.Key, field => field.Value!, StringComparer.Ordinal)
            : null;
        return new(key, reference.Value ?? reference.ItemId.ToString("D"), FormRecordReferencePolicy.Text(reference, culture).Text, fields);
    }

    private static StructuredIssueDatum OwnChoice(string key, CanonicalFormField question, JsonElement answer, string culture)
    {
        var choiceId = Guid.Parse(answer.GetString()!);
        var choice = question.Options?.SingleOrDefault(option => option.ChoiceId == choiceId)
            ?? throw new InvalidOperationException("The captured choice does not belong to its sealed version.");
        return new(key, choiceId.ToString("D"), FormRecordPresentationPolicy.Text(choice.Label, culture));
    }
}
