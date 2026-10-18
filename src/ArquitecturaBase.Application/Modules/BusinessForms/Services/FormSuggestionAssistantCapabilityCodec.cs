using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormSuggestionAssistantCapabilityCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    private static object ValueSchema => FormAssistantCapabilityCodec.ValueSchema;
    public static string ReadInputSchemaJson { get; } = JsonSerializer.Serialize(Obj(), Options);
    public static string InputSchemaJson { get; } = JsonSerializer.Serialize(Obj(("sourceRecordId", Str()), ("suggestionId", Str()),
        ("values", Arr(ValueSchema, 40)), ("reason", Str())), Options);
    public static string ReadSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("complete")), ("recipientUserId", Str()),
        ("culture", Str("es", "en")), ("isComplete", Bool()), ("sourceRecordId", Str()), ("suggestionSetId", NullableStr()),
        ("suggestionRevision", Int()), ("sourceName", Str()), ("sourceNumber", Str()),
        ("sourceValues", Arr(Obj(("fieldId", Str()), ("type", Str("shortText", "longText", "decimal", "date", "boolean", "choice")),
            ("label", Str()), ("value", NullableStr())), 40)),
        ("suggestions", Arr(Obj(("suggestionId", Str()), ("form", FormAssistantCapabilityCodec.CandidateSchema),
            ("mappedValues", Arr(ValueSchema, 40)), ("createsAutomaticLoop", Bool())), 20))), Options);
    public static string ProposalSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("proposed")), ("readOrdinal", Int()),
        ("sourceRecordId", Str()), ("suggestionSetId", Str()), ("expectedSuggestionRevision", Int()), ("suggestionId", Str()),
        ("definitionId", Str()), ("versionId", Str()), ("expectedDefinitionRevision", Int()), ("values", Arr(ValueSchema, 40)), ("reason", Str())), Options);

    public static Result<FormSuggestionAssistantRegistrationArguments> ParseArguments(string json) => Parse<FormSuggestionAssistantRegistrationArguments>(json,
        root => Has(root, "sourceRecordId", "suggestionId", "values", "reason") && FormAssistantCapabilityCodec.ValueShape(root.GetProperty("values")),
        value => value.SourceRecordId != Guid.Empty && value.SuggestionId != Guid.Empty
            && FormAssistantCapabilityCodec.ValidArgumentValues(value.Values) && FormAssistantCapabilityCodec.Text(value.Reason, 500));

    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string json)
    {
        if (capability.Version != BusinessFormSuggestionAssistantCapabilitySource.Version) return FormRecordErrors.InvalidResponse;
        if (capability.Id == BusinessFormSuggestionAssistantCapabilitySource.CandidatesId)
        {
            var result = Parse<FormSuggestionAssistantCandidatesPayload>(json, ReadShape, ValidRead);
            return result.IsSuccess ? result.Value : result.Error;
        }
        if (capability.Id == BusinessFormSuggestionAssistantCapabilitySource.RegistrationId)
        {
            var result = Parse<FormSuggestionAssistantRegistrationPayload>(json,
                root => Has(root, "state", "readOrdinal", "sourceRecordId", "suggestionSetId", "expectedSuggestionRevision", "suggestionId", "definitionId", "versionId", "expectedDefinitionRevision", "values", "reason")
                    && FormAssistantCapabilityCodec.ValueShape(root.GetProperty("values")), ValidProposal);
            return result.IsSuccess ? result.Value : result.Error;
        }
        return FormRecordErrors.InvalidResponse;
    }

    internal static bool ValidRead(FormSuggestionAssistantCandidatesPayload value) => value.State == "complete" && value.IsComplete
        && value.RecipientUserId != Guid.Empty && value.Culture is "es" or "en" && value.SourceRecordId != Guid.Empty
        && (value.SuggestionSetId.HasValue ? value.SuggestionSetId != Guid.Empty && value.SuggestionRevision is >= 1 and <= 9007199254740991
            : value.SuggestionRevision == 0 && value.Suggestions is { Count: 0 })
        && FormAssistantCapabilityCodec.Text(value.SourceName, 120) && FormAssistantCapabilityCodec.Text(value.SourceNumber, 40)
        && value.SourceValues is { Count: <= 40 } source && source.Select(field => field.FieldId).Distinct().Count() == source.Count
        && source.All(field => field is not null && field.FieldId != Guid.Empty && FormSuggestionPolicy.IsScalar(field.Type)
            && FormAssistantCapabilityCodec.Text(field.Label, 160) && (field.Value is null || FormAssistantCapabilityCodec.Text(field.Value, 10000)))
        && value.Suggestions is { Count: <= 20 } suggestions && suggestions.Select(row => row.SuggestionId).Distinct().Count() == suggestions.Count
        && suggestions.All(row => row is not null && row.SuggestionId != Guid.Empty && FormAssistantCapabilityCodec.ValidCandidate(row.Form)
            && FormAssistantCapabilityCodec.ValidValues(row.MappedValues)
            && FormAssistantObservationPolicy.Normalize(row.Form, row.MappedValues, value.Culture).IsSuccess);

    internal static bool ValidProposal(FormSuggestionAssistantRegistrationPayload value) => value.State == "proposed" && value.ReadOrdinal >= 1
        && value.SourceRecordId != Guid.Empty && value.SuggestionSetId != Guid.Empty && value.SuggestionId != Guid.Empty
        && value.DefinitionId != Guid.Empty && value.VersionId != Guid.Empty
        && value.ExpectedSuggestionRevision is >= 1 and <= 9007199254740991 && value.ExpectedDefinitionRevision is >= 1 and <= 9007199254740991
        && FormAssistantCapabilityCodec.ValidValues(value.Values) && FormAssistantCapabilityCodec.Text(value.Reason, 500);

    private static bool ReadShape(JsonElement value) => Has(value, "state", "recipientUserId", "culture", "isComplete", "sourceRecordId", "suggestionSetId", "suggestionRevision", "sourceName", "sourceNumber", "sourceValues", "suggestions")
        && value.GetProperty("sourceValues").ValueKind == JsonValueKind.Array
        && value.GetProperty("sourceValues").EnumerateArray().All(field => Has(field, "fieldId", "type", "label", "value"))
        && value.GetProperty("suggestions").ValueKind == JsonValueKind.Array
        && value.GetProperty("suggestions").EnumerateArray().All(row => Has(row, "suggestionId", "form", "mappedValues", "createsAutomaticLoop")
            && FormAssistantCapabilityCodec.CandidateShape(row.GetProperty("form")) && FormAssistantCapabilityCodec.ValueShape(row.GetProperty("mappedValues")));

    private static Result<T> Parse<T>(string json, Func<JsonElement, bool> shape, Func<T, bool> valid) where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!shape(document.RootElement)) return FormRecordErrors.InvalidResponse;
            var value = document.RootElement.Deserialize<T>(Options);
            return value is not null && valid(value) ? value : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }
    private static bool Has(JsonElement value, params string[] properties) => FormAssistantCapabilityCodec.Has(value, properties);
    private static object Obj(params (string Name, object Schema)[] fields) => new
    { type = "object", properties = fields.ToDictionary(field => field.Name, field => field.Schema, StringComparer.Ordinal), required = fields.Select(field => field.Name).ToArray(), additionalProperties = false };
    private static object Arr(object items, int maximum) => new { type = "array", items, maxItems = maximum };
    private static object Int() => new { type = "integer" };
    private static object Bool() => new { type = "boolean" };
    private static object NullableStr() => new { type = new[] { "string", "null" } };
    private static object Str(params string[] values) => values.Length == 0 ? (object)new { type = "string" } : new { type = "string", @enum = values };
}
