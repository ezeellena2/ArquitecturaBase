using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The bound data of a response for its issuer, by the datum keys its standard gave (BI1-A): what the response left empty does not
/// come, and the block of rows brings its items with their own bound questions. Texts go in the culture of the form. An option of
/// a catalog goes by its code, and so does a captured field that names an option of another catalog: the issuer knows codes. The
/// toggles of a bound question (IV2-A) come after, each by its own key, from the version the response was captured with.
/// </summary>
internal static class FormIssueDataPolicy
{
    /// <summary>The catalog options the response names, chosen or in a captured field of a chosen one, whose codes the issuer needs.</summary>
    public static IReadOnlyList<Guid> ReferencedItemIds(FormRecordStoredRow row) => ReferencedItemIds(row.References ?? []);

    public static IReadOnlyList<Guid> ReferencedItemIds(IReadOnlyList<FormRecordReferenceRow> references) => references.SelectMany(reference =>
        new[] { reference.ItemId }.Concat(reference.FieldValuesJson is { } json
            ? FormDraftPolicy.Read<FormCatalogCapturedField[]>(json).Where(field => field.Type == "catalog")
                .Select(field => Guid.TryParse(FormCatalogFieldPolicy.Input(field.Value), out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty)
            : [])).Distinct().ToArray();

    public static IReadOnlyList<StructuredIssueDatum> Read(FormRecordStoredRow row, IReadOnlyDictionary<string, Guid> bound, string culture,
        IReadOnlyDictionary<Guid, string> codes) => Read(row.SchemaJson, row.ValuesJson, row.References ?? [], bound, culture, codes);

    /// <summary>The same data from what a response keeps, also before it is sent (the «¿La emito?» of the app).</summary>
    public static IReadOnlyList<StructuredIssueDatum> Read(string schemaJson, string valuesJson, IReadOnlyList<FormRecordReferenceRow> references,
        IReadOnlyDictionary<string, Guid> bound, string culture, IReadOnlyDictionary<Guid, string> codes)
    {
        var schema = FormDraftPolicy.Read<FormVersionSchema>(schemaJson);
        var keys = bound.ToDictionary(pair => pair.Value, pair => pair.Key);
        using var document = JsonDocument.Parse(valuesJson);
        var answers = Answers(document.RootElement);
        var data = new List<StructuredIssueDatum>();
        foreach (var question in schema.Questions.Where(question => question.Repeated != true && keys.ContainsKey(question.FieldId)))
        {
            if (!answers.TryGetValue(question.FieldId, out var answer)) continue;
            if (question.Type != "group")
            {
                if (Datum(keys[question.FieldId], question, answer, references, culture, codes) is { } datum) data.Add(datum);
                continue;
            }
            var block = schema.Questions.SkipWhile(value => value.FieldId != question.FieldId).Skip(1)
                .TakeWhile(value => value.Repeated == true).Where(value => keys.ContainsKey(value.FieldId)).ToArray();
            var items = answer.EnumerateArray().Select((item, position) =>
            {
                var itemAnswers = Answers(item);
                var own = references.Where(reference => reference.Row == position).ToArray();
                return (IReadOnlyList<StructuredIssueDatum>)block.Where(value => itemAnswers.ContainsKey(value.FieldId))
                    .Select(value => Datum(keys[value.FieldId], value, itemAnswers[value.FieldId], own, culture, codes))
                    .OfType<StructuredIssueDatum>().ToArray();
            }).ToArray();
            data.Add(new StructuredIssueDatum(keys[question.FieldId], null, Items: items));
        }
        // IV2-A: each toggle a bound question has goes as a datum of its own, "true" or "false", answered or not.
        data.AddRange(schema.Questions.Where(question => keys.ContainsKey(question.FieldId)).SelectMany(question => question.Toggles ?? [])
            .Select(toggle => new StructuredIssueDatum(toggle.Key, toggle.Value ? "true" : "false")));
        return data;
    }

    private static Dictionary<Guid, JsonElement> Answers(JsonElement values) =>
        values.EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value").Clone());

    private static StructuredIssueDatum? Datum(string key, CanonicalFormField question, JsonElement answer,
        IReadOnlyList<FormRecordReferenceRow> references, string culture, IReadOnlyDictionary<Guid, string> codes) => question.Type switch
    {
        "shortText" or "longText" or "date" => new(key, answer.GetString()),
        "decimal" => new(key, answer.GetDecimal().ToString("0.############################", CultureInfo.InvariantCulture)),
        "boolean" => new(key, answer.GetBoolean() ? "true" : "false"),
        "choice" => question.Source?.Catalog is { } catalogId ? CatalogChoice(key, question, catalogId, answer, references, culture, codes) : OwnChoice(key, question, answer, culture),
        _ => null,
    };

    private static StructuredIssueDatum CatalogChoice(string key, CanonicalFormField question, Guid catalogId, JsonElement answer,
        IReadOnlyList<FormRecordReferenceRow> references, string culture, IReadOnlyDictionary<Guid, string> codes)
    {
        var reference = references.Single(value => value.FieldId == question.FieldId && value.CatalogId == catalogId && value.ItemId == answer.GetGuid());
        var fields = reference.FieldValuesJson is { } json
            ? FormDraftPolicy.Read<FormCatalogCapturedField[]>(json).Select(field => (field.Key, Value: FieldValue(field, codes)))
                .Where(field => field.Value is not null).ToDictionary(field => field.Key, field => field.Value!, StringComparer.Ordinal)
            : null;
        return new(key, codes.GetValueOrDefault(reference.ItemId) ?? reference.Value ?? reference.ItemId.ToString("D"),
            FormRecordReferencePolicy.Text(reference, culture).Text, fields);
    }

    private static string? FieldValue(FormCatalogCapturedField field, IReadOnlyDictionary<Guid, string> codes)
    {
        var value = FormCatalogFieldPolicy.Input(field.Value);
        return field.Type == "catalog" && Guid.TryParse(value, out var id) ? codes.GetValueOrDefault(id) ?? value : value;
    }

    private static StructuredIssueDatum OwnChoice(string key, CanonicalFormField question, JsonElement answer, string culture)
    {
        var choiceId = Guid.Parse(answer.GetString()!);
        var choice = question.Options?.SingleOrDefault(option => option.ChoiceId == choiceId)
            ?? throw new InvalidOperationException("The captured choice does not belong to its sealed version.");
        return new(key, choiceId.ToString("D"), FormRecordPresentationPolicy.Text(choice.Label, culture));
    }
}
