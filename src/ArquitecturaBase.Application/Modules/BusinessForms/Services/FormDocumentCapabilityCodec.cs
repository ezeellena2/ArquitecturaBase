using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

/// <summary>
/// The exact schemas and the closed codec of the documents capabilities (decision 8): the two reads (candidates in a channel, the fixed
/// form in the inbox) and propose-entry. A proposal is normalized against the latest read of the turn that showed its forms; a problem
/// goes back to the model as a rejection with its paths, never trimmed: unknown ids, questions or types, options not offered, a value
/// for a catalog question (the server fills them), a second candidate on «sure» or a type of document with personal data.
/// </summary>
internal static class FormDocumentCapabilityCodec
{
    public const int ResponseBudget = 16384;
    public const int MaxForms = 100;
    public const int MaxQuestions = 40;
    internal static readonly string[] QuestionTypes = ["shortText", "longText", "decimal", "date", "boolean", "choice"];
    private static readonly string[] Outcomes = [FormDocumentEntryProposal.Sure, FormDocumentEntryProposal.Doubt, FormDocumentEntryProposal.Unknown];
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    public static string EmptyInputSchemaJson { get; } = "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}";
    private static readonly object QuestionSchema = Obj(("fieldId", Str()), ("type", Str(QuestionTypes)), ("label", Str()), ("required", Bool()),
        ("maxLength", Int(true)), ("choices", Arr(Obj(("id", Str()), ("text", Str())), 200)), ("catalog", NullableStr()));
    private static readonly object CandidateSchema = Obj(("definitionId", Str()), ("versionId", Str()), ("definitionRevision", Int()), ("name", Str()),
        ("recognition", NullableStr()), ("questions", Arr(QuestionSchema, MaxQuestions)));
    public static string ReadSchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("complete", "partial")), ("userId", Str()),
        ("culture", Str("es", "en")), ("isComplete", Bool()), ("forms", Arr(CandidateSchema, MaxForms))), Options);
    private static readonly object ArgumentValueSchema = Obj(("fieldId", Str()), ("value", new
    {
        type = new[] { "string", "null" },
        description = "Exactly as read, within the question limit; null when it was not read. Decimal: invariant digits with an optional minus sign and dot, for example 150000.50, no grouping separators. Date: yyyy-MM-dd. Boolean: true or false. Choice with options: the exact observed option GUID in D format. A catalog question is always null: the server fills it.",
    }), ("doubtful", Bool()));
    public static string EntryInputSchemaJson { get; } = JsonSerializer.Serialize(Obj(("outcome", Str(Outcomes)),
        ("candidates", Arr(Obj(("definitionId", Str()), ("versionId", Str()), ("values", Arr(ArgumentValueSchema, MaxQuestions))), 3)),
        ("documentType", Str())), Options);
    public static string EntrySchemaJson { get; } = JsonSerializer.Serialize(Obj(("state", Str("proposed")), ("readOrdinal", Int()), ("outcome", Str(Outcomes)),
        ("candidates", Arr(Obj(("definitionId", Str()), ("versionId", Str()), ("expectedDefinitionRevision", Int()),
            ("values", Arr(Obj(("fieldId", Str()), ("type", Str(QuestionTypes)), ("value", NullableStr()), ("doubtful", Bool())), MaxQuestions))), 3)),
        ("documentType", Str())), Options);

    public static int ResponseBytes(AssistantCapabilityPayload payload, string state, bool isProposal) =>
        FormAssistantCapabilityCodec.ResponseBytes(payload, state, isProposal);

    public static Result<FormDocumentEntryArguments> ParseArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!Has(root, "outcome", "candidates", "documentType") || root.GetProperty("candidates") is not { ValueKind: JsonValueKind.Array } candidates
                || candidates.EnumerateArray().Any(candidate => !Has(candidate, "definitionId", "versionId", "values")
                    || candidate.GetProperty("values") is not { ValueKind: JsonValueKind.Array } values
                    || values.EnumerateArray().Any(value => !Has(value, "fieldId", "value", "doubtful"))))
                return FormRecordErrors.InvalidResponse;
            var value = root.Deserialize<FormDocumentEntryArguments>(Options);
            return value is { Outcome: not null, DocumentType: not null, Candidates: not null }
                && value.Candidates.All(candidate => candidate is { Values: not null } && candidate.Values.All(item => item is not null))
                ? value : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    /// <summary>
    /// The proposal normalized against the latest documents read of the turn, or the problems to fix with their paths: the outcome and
    /// its number of candidates, each candidate observed in that read, each value of a question it showed, canonical for its type and
    /// within its options, and the type of document generic and without personal data.
    /// </summary>
    public static (FormDocumentEntryProposal? Proposal, IReadOnlyList<FormDocumentEntryIssue> Issues) Normalize(FormDocumentEntryArguments arguments,
        FormObservedDocumentRead? observed)
    {
        var issues = new List<FormDocumentEntryIssue>();
        var expected = arguments.Outcome switch
        {
            FormDocumentEntryProposal.Sure => (Minimum: 1, Maximum: 1),
            FormDocumentEntryProposal.Doubt => (Minimum: 2, Maximum: 3),
            FormDocumentEntryProposal.Unknown => (Minimum: 0, Maximum: 0),
            _ => (Minimum: -1, Maximum: -1),
        };
        if (expected.Minimum < 0) issues.Add(new("outcome", FormDocumentEntryIssue.Invalid));
        else if (arguments.Candidates.Count < expected.Minimum || arguments.Candidates.Count > expected.Maximum)
            issues.Add(new("candidates", FormDocumentEntryIssue.Count));
        var documentType = arguments.DocumentType.Trim();
        if (!FormAssistantCapabilityCodec.Text(documentType, FormDocumentEntryProposal.MaxDocumentTypeLength))
            issues.Add(new("documentType", FormDocumentEntryIssue.Invalid));
        else if (FormMissingRequestPrivacyPolicy.HasContactDetails(documentType, null) || documentType.Count(char.IsAsciiDigit) >= 4)
            issues.Add(new("documentType", FormDocumentEntryIssue.PersonalData));
        var candidates = new List<FormDocumentEntryCandidate>();
        for (var index = 0; index < arguments.Candidates.Count; index++)
        {
            var candidate = arguments.Candidates[index];
            var path = $"candidates[{index}]";
            if (arguments.Candidates.Take(index).Any(previous => previous.DefinitionId == candidate.DefinitionId))
            {
                issues.Add(new(path + ".definitionId", FormDocumentEntryIssue.Repeated));
                continue;
            }
            var form = observed?.Payload.Forms.SingleOrDefault(form => form.DefinitionId == candidate.DefinitionId && form.VersionId == candidate.VersionId);
            if (form is null)
            {
                issues.Add(new(path, FormDocumentEntryIssue.NotObserved));
                continue;
            }
            var values = new List<FormDocumentEntryValue>();
            for (var position = 0; position < candidate.Values.Count; position++)
            {
                var value = candidate.Values[position];
                var valuePath = $"{path}.values[{position}]";
                if (candidate.Values.Take(position).Any(previous => previous.FieldId == value.FieldId))
                {
                    issues.Add(new(valuePath + ".fieldId", FormDocumentEntryIssue.Repeated));
                    continue;
                }
                var question = form.Questions.SingleOrDefault(question => question.FieldId == value.FieldId);
                if (question is null)
                {
                    issues.Add(new(valuePath + ".fieldId", FormDocumentEntryIssue.UnknownQuestion));
                    continue;
                }
                if (value.Value is null)
                {
                    values.Add(new(question.FieldId, question.Type, null, value.Doubtful));
                    continue;
                }
                if (question.Catalog is not null)
                {
                    issues.Add(new(valuePath + ".value", FormDocumentEntryIssue.FilledByServer));
                    continue;
                }
                var canonical = Canonical(question, value.Value);
                if (canonical.IsFailure)
                {
                    issues.Add(new(valuePath + ".value", question.Type == "choice" ? FormDocumentEntryIssue.NotOffered : FormDocumentEntryIssue.Invalid));
                    continue;
                }
                values.Add(new(question.FieldId, question.Type, canonical.Value, value.Doubtful));
            }
            candidates.Add(new(form.DefinitionId, form.VersionId, form.DefinitionRevision, values));
        }
        if (issues.Count != 0 || observed is null && arguments.Candidates.Count != 0) return (null, issues.Count == 0 ? [new("candidates", FormDocumentEntryIssue.NotObserved)] : issues);
        return (new FormDocumentEntryProposal("proposed", observed?.ReadOrdinal ?? 0, arguments.Outcome, candidates, documentType), []);
    }

    /// <summary>A value as the prefill takes it (<see cref="FormScalarPrefillPolicy"/>), against the question the read showed.</summary>
    public static Result<string> Canonical(FormDocumentQuestion question, string value)
    {
        var schema = new StructuredFormQuestion(question.FieldId, "field_" + question.FieldId.ToString("N"), question.Type,
            new Dictionary<string, string> { ["es"] = question.Label }, null, question.Required, question.MaxLength,
            question.Type == "choice" ? question.Choices.Select(choice => new StructuredFormChoice(choice.Id, new Dictionary<string, string> { ["es"] = choice.Text })).ToArray() : null);
        return FormScalarPrefillPolicy.Normalize(schema, value, []);
    }

    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference reference, string json)
    {
        if (reference.Version != BusinessFormDocumentAssistantCapabilitySource.Version) return FormRecordErrors.InvalidResponse;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (reference.Id is BusinessFormDocumentAssistantCapabilitySource.CandidatesId or BusinessFormDocumentAssistantCapabilitySource.FormId)
            {
                if (!ReadShape(root)) return FormRecordErrors.InvalidResponse;
                var read = root.Deserialize<FormDocumentReadPayload>(Options);
                return read is not null && ValidRead(read) ? read : FormRecordErrors.InvalidResponse;
            }
            if (reference.Id != BusinessFormDocumentAssistantCapabilitySource.ProposeEntryId) return FormRecordErrors.InvalidResponse;
            if (Has(root, "issues"))
            {
                var rejection = root.Deserialize<FormDocumentEntryRejection>(Options);
                return rejection is { Issues: { Count: > 0 and <= 50 } issues } && issues.All(issue => issue is { Path.Length: > 0 and <= 120 }
                    && FormDocumentEntryIssue.Codes.Contains(issue.Code, StringComparer.Ordinal)) ? rejection : FormRecordErrors.InvalidResponse;
            }
            if (!ProposalShape(root)) return FormRecordErrors.InvalidResponse;
            var proposal = root.Deserialize<FormDocumentEntryProposal>(Options);
            return proposal is not null && ValidProposal(proposal) ? proposal : FormRecordErrors.InvalidResponse;
        }
        catch (JsonException) { return FormRecordErrors.InvalidResponse; }
    }

    internal static bool ValidRead(FormDocumentReadPayload value) => value.State == (value.IsComplete ? "complete" : "partial") && value.UserId != Guid.Empty
        && value.Culture is "es" or "en" && value.Forms is { Count: <= MaxForms } forms
        && forms.Select(form => form.DefinitionId).Distinct().Count() == forms.Count && forms.All(ValidCandidate);

    internal static bool ValidCandidate(FormDocumentCandidate form) => form.DefinitionId != Guid.Empty && form.VersionId != Guid.Empty
        && form.DefinitionRevision is >= 1 and <= 9007199254740991 && FormAssistantCapabilityCodec.Text(form.Name, 120)
        && (form.Recognition is null || FormAssistantCapabilityCodec.Text(form.Recognition, FormDocumentReading.MaxRecognitionLength))
        && form.Questions is { Count: <= MaxQuestions } questions && questions.Select(question => question.FieldId).Distinct().Count() == questions.Count
        && questions.All(question => question.FieldId != Guid.Empty && QuestionTypes.Contains(question.Type, StringComparer.Ordinal)
            && FormAssistantCapabilityCodec.Text(question.Label, 160, graphemes: true)
            && (question.Type is "shortText" or "longText" ? question.MaxLength is >= 1 and <= 10000 : question.MaxLength is null)
            && (question.Type == "choice" || question.Choices.Count == 0 && question.Catalog is null)
            && (question.Catalog is null || question.Choices.Count == 0 && FormAssistantCapabilityCodec.Text(question.Catalog, 160, graphemes: true))
            && question.Choices.Count <= 200 && question.Choices.Select(choice => choice.Id).Distinct().Count() == question.Choices.Count
            && question.Choices.All(choice => choice.Id != Guid.Empty && FormAssistantCapabilityCodec.Text(choice.Text, 160, graphemes: true)));

    internal static bool ValidProposal(FormDocumentEntryProposal value) => value.State == "proposed" && value.ReadOrdinal >= 0
        && Outcomes.Contains(value.Outcome, StringComparer.Ordinal) && FormAssistantCapabilityCodec.Text(value.DocumentType, FormDocumentEntryProposal.MaxDocumentTypeLength)
        && value.Candidates is { } candidates && candidates.Count == value.Outcome switch
        {
            FormDocumentEntryProposal.Sure => 1,
            FormDocumentEntryProposal.Unknown => 0,
            _ => Math.Clamp(candidates.Count, 2, 3),
        }
        && (candidates.Count == 0 || value.ReadOrdinal >= 1)
        && candidates.Select(candidate => candidate.DefinitionId).Distinct().Count() == candidates.Count
        && candidates.All(candidate => candidate.DefinitionId != Guid.Empty && candidate.VersionId != Guid.Empty
            && candidate.ExpectedDefinitionRevision is >= 1 and <= 9007199254740991 && candidate.Values is { Count: <= MaxQuestions } values
            && values.Select(item => item.FieldId).Distinct().Count() == values.Count
            && values.All(item => item.FieldId != Guid.Empty && QuestionTypes.Contains(item.Type, StringComparer.Ordinal)
                && (item.Value is null || item.Value.Length is > 0 and <= 10000 && ArquitecturaBase.Domain.Common.StorableText.IsStorable(item.Value))));

    private static bool ReadShape(JsonElement value)
    {
        if (!Has(value, "state", "userId", "culture", "isComplete", "forms") || value.GetProperty("forms").ValueKind != JsonValueKind.Array) return false;
        foreach (var form in value.GetProperty("forms").EnumerateArray())
        {
            if (!Has(form, "definitionId", "versionId", "definitionRevision", "name", "recognition", "questions")
                || form.GetProperty("questions").ValueKind != JsonValueKind.Array) return false;
            foreach (var question in form.GetProperty("questions").EnumerateArray())
                if (!Has(question, "fieldId", "type", "label", "required", "maxLength", "choices", "catalog")
                    || question.GetProperty("choices").ValueKind != JsonValueKind.Array
                    || question.GetProperty("choices").EnumerateArray().Any(choice => !Has(choice, "id", "text"))) return false;
        }
        return true;
    }

    private static bool ProposalShape(JsonElement value) => Has(value, "state", "readOrdinal", "outcome", "candidates", "documentType")
        && value.GetProperty("candidates").ValueKind == JsonValueKind.Array
        && value.GetProperty("candidates").EnumerateArray().All(candidate => Has(candidate, "definitionId", "versionId", "expectedDefinitionRevision", "values")
            && candidate.GetProperty("values").ValueKind == JsonValueKind.Array
            && candidate.GetProperty("values").EnumerateArray().All(item => Has(item, "fieldId", "type", "value", "doubtful")));

    private static bool Has(JsonElement value, params string[] properties) => FormAssistantCapabilityCodec.Has(value, properties);
    private static object Obj(params (string Name, object Schema)[] fields) => new
    { type = "object", properties = fields.ToDictionary(field => field.Name, field => field.Schema, StringComparer.Ordinal), required = fields.Select(field => field.Name).ToArray(), additionalProperties = false };
    private static object Arr(object items, int maximum) => new { type = "array", items, maxItems = maximum };
    private static Dictionary<string, object> Int(bool nullable = false) => new() { ["type"] = nullable ? new[] { "integer", "null" } : (object)"integer" };
    private static object Bool() => new { type = "boolean" };
    private static object NullableStr() => new { type = new[] { "string", "null" } };
    private static object Str(params string[] values) => values.Length == 0 ? new { type = "string" } : new { type = "string", @enum = values };
}

/// <summary>The latest documents read of a turn, its ordinal and what it showed.</summary>
internal sealed record FormObservedDocumentRead(int ReadOrdinal, FormDocumentReadPayload Payload)
{
    public override string ToString() => "FormObservedDocumentRead { Content = [REDACTED] }";
}
