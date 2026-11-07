using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The answers of a capture checked against its sealed version, in the order of its questions; null when one is not valid. A
/// block of rows arrives under its key as a JSON array with an object per item, <c>{"&lt;key of a question of the block&gt;":
/// "&lt;canonical value&gt;"}</c>, as «elegir varias» sends its array: between the minimum and the maximum of the block, and each
/// item answers the questions of its block as loose questions, with the same offer of a catalog for every item.
/// </summary>
internal static class FormRecordAnswerPolicy
{
    public static IReadOnlyList<FormRecordAnswer>? Normalize(FormVersionSchema schema, IReadOnlyDictionary<string, string> answers,
        IReadOnlyList<StructuredFormCatalogOffer> offers)
    {
        var questions = schema.Questions;
        if (schema.Format != 1 || FormDraftPolicy.QuestionCount(questions) > 40 || questions.Count > 41) return null;
        // A question of the block is answered only inside its items.
        if (answers.Keys.Any(key => !questions.Any(question => question.Key == key && question.Repeated != true))) return null;
        var result = new List<FormRecordAnswer>();
        for (var index = 0; index < questions.Count; index++)
        {
            var question = questions[index];
            if (question.Type is "photo" or "document" || question.Repeated == true) continue;
            if (!answers.TryGetValue(question.Key, out var raw))
            {
                if (question.Required) return null;
                continue;
            }
            if (question.Type == "group")
            {
                var block = questions.Skip(index + 1).TakeWhile(child => child.Repeated == true).ToArray();
                var rows = Rows(question, block, raw, offers);
                if (rows is null) return null;
                result.Add(new(question.FieldId, question.Type, null, null, null, null, null) { Rows = rows });
                continue;
            }
            if (question.Type == "multiChoice")
            {
                var choices = MultipleChoices(question, raw, offers);
                if (choices is null) return null;
                result.Add(new(question.FieldId, question.Type, null, null, null, null, null, choices));
                continue;
            }
            if (!TryScalar(question, raw, offers, out var answer)) return null;
            if (answer is not null) result.Add(answer);
        }
        return result;
    }

    private static List<IReadOnlyList<FormRecordAnswer>>? Rows(CanonicalFormField group, IReadOnlyList<CanonicalFormField> block,
        string raw, IReadOnlyList<StructuredFormCatalogOffer> offers)
    {
        if (group.Rows is not { } limits) return null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            var count = json.RootElement.GetArrayLength();
            if (count < limits.MinRows || count > limits.MaxRows || count > BusinessFormRecordValue.MaxRows) return null;
            var rows = new List<IReadOnlyList<FormRecordAnswer>>(count);
            foreach (var element in json.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) return null;
                var item = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                    if (property.Value.ValueKind != JsonValueKind.String || block.All(question => question.Key != property.Name)
                        || !item.TryAdd(property.Name, property.Value.GetString()!)) return null;
                var answers = new List<FormRecordAnswer>();
                foreach (var question in block)
                {
                    if (!item.TryGetValue(question.Key, out var value))
                    {
                        if (question.Required) return null;
                        continue;
                    }
                    if (!TryScalar(question, value, offers, out var answer)) return null;
                    if (answer is not null) answers.Add(answer);
                }
                rows.Add(answers);
            }
            return rows;
        }
        catch (JsonException) { return null; }
    }

    // True with a null answer for an optional question left empty.
    private static bool TryScalar(CanonicalFormField question, string raw, IReadOnlyList<StructuredFormCatalogOffer> offers,
        out FormRecordAnswer? answer)
    {
        answer = null;
        if (raw.Length == 0) return !question.Required;
        switch (question.Type)
        {
            case "shortText":
            case "longText":
                if (raw.Length > question.MaxLength || !StorableText.IsStorable(raw.AsSpan())) return false;
                answer = new(question.FieldId, question.Type, raw, null, null, null, null);
                return true;
            case "decimal":
                if (!decimal.TryParse(raw, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
                    || FormScalarPrefillPolicy.CanonicalDecimal(raw) != FormScalarPrefillPolicy.CanonicalDecimal(number.ToString("0.############################", CultureInfo.InvariantCulture))) return false;
                answer = new(question.FieldId, question.Type, null, number, null, null, null);
                return true;
            case "date":
                if (!DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return false;
                answer = new(question.FieldId, question.Type, null, null, date, null, null);
                return true;
            case "boolean":
                if (!bool.TryParse(raw, out var boolean)) return false;
                answer = new(question.FieldId, question.Type, null, null, null, boolean, null);
                return true;
            case "choice":
                if (!Guid.TryParseExact(raw, "D", out var choice) || !Offered(question, choice, offers)) return false;
                answer = new(question.FieldId, question.Type, null, null, null, null, choice);
                return true;
            default: return false;
        }
    }

    private static Guid[]? MultipleChoices(CanonicalFormField question, string raw, IReadOnlyList<StructuredFormCatalogOffer> offers)
    {
        if (question.Selection is not { } range || range.MinSelected < 0 || range.MaxSelected is < 1 or > 20
            || range.MinSelected > range.MaxSelected || question.Required != (range.MinSelected > 0)) return null;
        try
        {
            using var json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind != JsonValueKind.Array) return null;
            var count = json.RootElement.GetArrayLength();
            if (count < range.MinSelected || count > range.MaxSelected) return null;
            var choices = new Guid[count];
            var distinct = new HashSet<Guid>();
            for (var index = 0; index < count; index++)
            {
                var value = json.RootElement[index];
                if (value.ValueKind != JsonValueKind.String || !Guid.TryParseExact(value.GetString(), "D", out var choice)
                    || choice == Guid.Empty || !distinct.Add(choice) || !Offered(question, choice, offers)) return null;
                choices[index] = choice;
            }
            return choices;
        }
        catch (JsonException) { return null; }
    }

    // A catalog option comes from the offer of its question; an inline option, from the sealed version.
    private static bool Offered(CanonicalFormField question, Guid choice, IReadOnlyList<StructuredFormCatalogOffer> offers) =>
        question.Source?.Catalog is { } catalogId
            ? offers.SingleOrDefault(offer => offer.FieldId == question.FieldId && offer.CatalogId == catalogId)?.Options.Any(option => option.Id == choice) == true
            : question.Options?.Any(option => option.ChoiceId == choice) == true;
}

/// <summary>A checked answer; a block keeps its items, each with its answers in the order of the questions of the block.</summary>
internal sealed record FormRecordAnswer(Guid FieldId, string Type, string? Text, decimal? Decimal, DateOnly? Date, bool? Boolean,
    Guid? ChoiceId, Guid[]? ChoiceIds = null)
{
    /// <summary>The items of a block, each with its answers; null for any other question.</summary>
    public IReadOnlyList<IReadOnlyList<FormRecordAnswer>>? Rows { get; init; }

    // What ValuesJson keeps: a value, the array of chosen options or, for a block, an array per item with the same shape.
    public object Value => Type switch
    {
        "shortText" or "longText" => Text!, "decimal" => Decimal!.Value, "date" => Date!.Value,
        "boolean" => Boolean!.Value, "choice" => ChoiceId!.Value, "multiChoice" => ChoiceIds!,
        "group" => Rows!.Select(row => row.Select(answer => new { answer.FieldId, answer.Value }).ToArray()).ToArray(),
        _ => throw new InvalidOperationException("Unknown form answer type."),
    };
    public override string ToString() => "FormRecordAnswer { Content = [REDACTED] }";
}
