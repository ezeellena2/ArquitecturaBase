using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormAssistantCapabilityCodec
{
    private static readonly string[] ScalarTypes = ["shortText", "longText", "decimal", "date", "boolean", "choice"];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static readonly object ValueSchema = Obj(("fieldId", Str()), ("type", Str(ScalarTypes)), ("value", Str()));
    public static string InputSchemaJson { get; } = JsonSerializer.Serialize(Obj(("definitionId", Str()), ("versionId", Str()),
        ("values", Arr(ValueSchema, 40)), ("reason", Str())), Options);
    public static string ProposalSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("proposed")), ("readOrdinal", Int()),
        ("definitionId", Str()), ("versionId", Str()), ("expectedDefinitionRevision", Int()), ("values", Arr(ValueSchema, 40)), ("reason", Str())), Options);
    public static string ReadSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("complete")), ("recipientUserId", Str()),
        ("culture", Str("es", "en")), ("isComplete", new { type = "boolean" }), ("forms", Arr(Obj(
            ("definitionId", Str()), ("versionId", Str()), ("definitionRevision", Int()), ("name", Str()), ("offerWhen", Str()),
            ("originalCulture", Str("es", "en")), ("questions", Arr(Obj(("fieldId", Str()), ("type", Str(ScalarTypes)),
                ("label", Str()), ("maxLength", Int(true)), ("catalogId", new { type = new[] { "string", "null" } }),
                ("catalogRevision", Int(true)), ("choices", Arr(Obj(("id", Str()), ("text", Str())), 200))), 40))), 100))), Options);

    public static Result<FormAssistantRegistrationArguments> ParseArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Has(root, "definitionId", "versionId", "values", "reason") || !ValueShape(root.GetProperty("values"))) return FormRecordErrors.InvalidResponse;
            var value = root.Deserialize<FormAssistantRegistrationArguments>(Options);
            return value is not null && value.DefinitionId != Guid.Empty && value.VersionId != Guid.Empty && ValidValues(value.Values) && Text(value.Reason, 500)
                ? value : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    public static bool EmptyArguments(string json)
    {
        try { using var document = JsonDocument.Parse(json); return Has(document.RootElement); }
        catch (JsonException) { return false; }
    }

    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference reference, string json)
    {
        if (reference.Version != BusinessFormAssistantCapabilitySource.Version) return FormRecordErrors.InvalidResponse;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (reference.Id == BusinessFormAssistantCapabilitySource.CandidatesId && ReadShape(root))
            {
                var value = root.Deserialize<FormAssistantCandidatesPayload>(Options);
                return value is not null && ValidRead(value) ? value : FormRecordErrors.InvalidResponse;
            }
            if (reference.Id == BusinessFormAssistantCapabilitySource.RegistrationId && Has(root, "state", "readOrdinal", "definitionId", "versionId", "expectedDefinitionRevision", "values", "reason")
                && ValueShape(root.GetProperty("values")))
            {
                var value = root.Deserialize<FormAssistantRegistrationPayload>(Options);
                return value is not null && value.State == "proposed" && value.ReadOrdinal >= 1 && value.DefinitionId != Guid.Empty && value.VersionId != Guid.Empty
                    && value.ExpectedDefinitionRevision is >= 1 and <= 9007199254740991 && ValidValues(value.Values) && Text(value.Reason, 500)
                    ? value : FormRecordErrors.InvalidResponse;
            }
            return FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    internal static bool ValidRead(FormAssistantCandidatesPayload value) => value.State == "complete" && value.IsComplete && value.RecipientUserId != Guid.Empty
        && value.Culture is "es" or "en" && value.Forms.Count <= 100 && value.Forms.Select(form => form.DefinitionId).Distinct().Count() == value.Forms.Count
        && value.Forms.All(form => form.DefinitionId != Guid.Empty && form.VersionId != Guid.Empty && form.DefinitionRevision is >= 1 and <= 9007199254740991
            && form.OriginalCulture is "es" or "en" && Text(form.Name, 120) && Text(form.OfferWhen, 1000) && form.Questions.Count <= 40
            && form.Questions.Select(question => question.FieldId).Distinct().Count() == form.Questions.Count
            && form.Questions.All(question => question.FieldId != Guid.Empty && ScalarTypes.Contains(question.Type, StringComparer.Ordinal) && Text(question.Label, 160)
                && (question.Type is "shortText" or "longText" ? question.MaxLength is >= 1 and <= 10000 : question.MaxLength is null)
                && (question.Type == "choice" || question.Choices.Count == 0 && question.CatalogId is null)
                && question.CatalogId != Guid.Empty && (question.CatalogId.HasValue ? question.CatalogRevision is >= 1 : question.CatalogRevision is null)
                && question.Choices.Count <= 200 && question.Choices.Select(choice => choice.Id).Distinct().Count() == question.Choices.Count
                && question.Choices.All(choice => choice.Id != Guid.Empty && Text(choice.Text, 160))));

    internal static bool ValidValues(IReadOnlyList<FormAssistantValue> values) => values.Count <= 40
        && values.Select(value => value.FieldId).Distinct().Count() == values.Count
        && values.All(value => value.FieldId != Guid.Empty && ScalarTypes.Contains(value.Type, StringComparer.Ordinal) && value.Value is { Length: > 0 and <= 10000 } && StorableText.IsStorable(value.Value));

    internal static bool Text(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Length <= maximum && StorableText.IsStorable(value);

    private static bool ReadShape(JsonElement value)
    {
        if (!Has(value, "state", "recipientUserId", "culture", "isComplete", "forms") || value.GetProperty("forms").ValueKind != JsonValueKind.Array) return false;
        foreach (var form in value.GetProperty("forms").EnumerateArray())
        {
            if (!Has(form, "definitionId", "versionId", "definitionRevision", "name", "offerWhen", "originalCulture", "questions") || form.GetProperty("questions").ValueKind != JsonValueKind.Array) return false;
            foreach (var question in form.GetProperty("questions").EnumerateArray())
            {
                if (!Has(question, "fieldId", "type", "label", "maxLength", "catalogId", "catalogRevision", "choices") || question.GetProperty("choices").ValueKind != JsonValueKind.Array) return false;
                if (question.GetProperty("choices").EnumerateArray().Any(choice => !Has(choice, "id", "text"))) return false;
            }
        }
        return true;
    }
    private static bool ValueShape(JsonElement values) => values.ValueKind == JsonValueKind.Array && values.EnumerateArray().All(value => Has(value, "fieldId", "type", "value"));
    private static bool Has(JsonElement value, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == properties.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length && names.All(name => properties.Contains(name, StringComparer.Ordinal));
    }
    private static object Obj(params (string Name, object Schema)[] fields) => new
    { type = "object", properties = fields.ToDictionary(field => field.Name, field => field.Schema, StringComparer.Ordinal), required = fields.Select(field => field.Name).ToArray(), additionalProperties = false };
    private static object Arr(object items, int maximum) => new { type = "array", items, maxItems = maximum };
    private static Dictionary<string, object> Int(bool nullable = false) => new() { ["type"] = nullable ? new[] { "integer", "null" } : (object)"integer" };
    private static object Str(params string[] values)
    {
        if (values.Length == 0) return new { type = "string" };
        return new { type = "string", @enum = values };
    }
}

internal sealed record FormAssistantRegistrationArguments(Guid DefinitionId, Guid VersionId, IReadOnlyList<FormAssistantValue> Values, string Reason)
{ public override string ToString() => "FormAssistantRegistrationArguments { Content = [REDACTED] }"; }
