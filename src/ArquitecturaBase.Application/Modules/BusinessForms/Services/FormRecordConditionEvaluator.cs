using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
internal static class FormRecordConditionEvaluator
{
    public static bool Matches(IReadOnlyList<FormRecordCondition> conditions, FormActionRecordRow snapshot)
    {
        var row = snapshot.Response.Record; var schema = FormDraftPolicy.Read<FormVersionSchema>(row.SchemaJson);
        using var values = JsonDocument.Parse(row.ValuesJson);
        var answers = Answers(values.RootElement); var items = Items(schema, answers);
        var checks = conditions.Select(condition => (Condition: condition, Field: schema.Questions.SingleOrDefault(field => field.FieldId == condition.FieldId))).ToArray();
        // The conditions with a value on questions of the block of rows are met by one same item (RG5-A), as in «Filtros».
        static bool InItem((FormRecordCondition Condition, CanonicalFormField? Field) check) =>
            check.Field?.Repeated == true && check.Condition.Operator is not ("answered" or "unanswered" or "notAsked");
        var inItem = checks.Where(InItem).ToArray();
        return checks.Where(check => !InItem(check)).All(check => Matches(check.Condition, check.Field, check.Field?.Repeated == true ? items : [answers], row.Files ?? []))
            && (inItem.Length == 0 || items.Any(item => inItem.All(check => Matches(check.Condition, check.Field, [item], []))));
    }
    /// <summary>
    /// A condition on the answers of the response, or on those of each item for a question of the block: presence looks at all of
    /// them, as the sheet does, and a value is met by one of them.
    /// </summary>
    private static bool Matches(FormRecordCondition condition, CanonicalFormField? field, IReadOnlyList<Dictionary<Guid, JsonElement>> answers,
        IReadOnlyList<FormRecordFileRow> files)
    {
        var given = answers.Where(set => set.TryGetValue(condition.FieldId, out var answer) && !(answer.ValueKind == JsonValueKind.Array && answer.GetArrayLength() == 0))
            .Select(set => set[condition.FieldId]).ToArray();
        var present = given.Length != 0 || files.Any(file => file.FieldId == condition.FieldId);
        if (condition.Operator == "notAsked") return field is null;
        if (condition.Operator == "answered") return present;
        if (condition.Operator == "unanswered") return !present;
        return field is not null && given.Any(answer => Meets(field, condition, answer));
    }
    private static bool Meets(CanonicalFormField field, FormRecordCondition condition, JsonElement answer)
    {
        var expected = condition.Values![0];
        return field.Type switch
        {
            "shortText" or "longText" => condition.Operator == "contains" ? answer.GetString()!.Contains(expected, StringComparison.OrdinalIgnoreCase) : answer.GetString() == expected,
            "decimal" => Compare(answer.GetDecimal().CompareTo(decimal.Parse(expected, CultureInfo.InvariantCulture)), condition.Operator),
            "date" => Compare(DateOnly.ParseExact(answer.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).CompareTo(DateOnly.ParseExact(expected, "yyyy-MM-dd", CultureInfo.InvariantCulture)), condition.Operator),
            "boolean" => condition.Values.Contains(answer.GetBoolean() ? "true" : "false"),
            "multiChoice" => answer.EnumerateArray().Any(value => condition.Values.Any(expected => Guid.Parse(expected) == value.GetGuid())),
            "choice" => condition.Values.Any(value => Guid.Parse(value) == answer.GetGuid()),
            _ => false,
        };
    }
    private static Dictionary<Guid, JsonElement> Answers(JsonElement values) =>
        values.EnumerateArray().ToDictionary(value => value.GetProperty("fieldId").GetGuid(), value => value.GetProperty("value"));
    /// <summary>The answers of each item of the block of rows of the version, in order; none without the block or its answer.</summary>
    private static Dictionary<Guid, JsonElement>[] Items(FormVersionSchema schema, Dictionary<Guid, JsonElement> answers) =>
        schema.Questions.FirstOrDefault(question => question.Type == "group") is { } group && answers.TryGetValue(group.FieldId, out var items)
            ? items.EnumerateArray().Select(Answers).ToArray() : [];
    private static bool Compare(int compared, string operation) => operation switch { "gte" => compared >= 0, "lte" => compared <= 0, _ => compared == 0 };
}
