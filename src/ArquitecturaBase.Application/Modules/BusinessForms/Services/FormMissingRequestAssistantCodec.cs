using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;

internal static class FormMissingRequestAssistantCodec
{
    public const string ArgumentsSchema = """
        {"type":"object","properties":{"existingTopicId":{"type":["string","null"]},"name":{"type":["string","null"],"maxLength":120},"description":{"type":["string","null"],"maxLength":500}},"required":["existingTopicId","name","description"],"additionalProperties":false}
        """;
    public const string ProposalSchema = """
        {"type":"object","properties":{"state":{"type":"string","enum":["proposed"]},"readOrdinal":{"type":"integer","minimum":1},"existingTopicId":{"type":["string","null"]},"name":{"type":["string","null"],"maxLength":120},"description":{"type":["string","null"],"maxLength":500}},"required":["state","readOrdinal","existingTopicId","name","description"],"additionalProperties":false}
        """;
    public static Result<FormMissingRequestArguments> ParseArguments(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!FormAssistantCapabilityCodec.Has(document.RootElement, "existingTopicId", "name", "description")) return FormMissingRequestErrors.ContentInvalid;
            var value = document.RootElement.Deserialize<FormMissingRequestArguments>(JsonSerializerOptions.Web);
            return value is not null && Valid(value.ExistingTopicId, value.Name, value.Description) ? value : FormMissingRequestErrors.ContentInvalid;
        }
        catch (JsonException) { return FormMissingRequestErrors.ContentInvalid; }
    }
    public static Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference reference, string json)
    {
        if (reference is not { Id: BusinessFormMissingRequestAssistantCapabilitySource.ProposeId, Version: 1 }) return FormMissingRequestErrors.ContentInvalid;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!FormAssistantCapabilityCodec.Has(document.RootElement, "state", "readOrdinal", "existingTopicId", "name", "description")) return FormMissingRequestErrors.ContentInvalid;
            var value = document.RootElement.Deserialize<MissingFormRequestProposalPayload>(JsonSerializerOptions.Web);
            return value is not null && value.State == "proposed" && value.ReadOrdinal >= 1
                && Valid(value.ExistingTopicId, value.Name, value.Description) ? value : FormMissingRequestErrors.ContentInvalid;
        }
        catch (JsonException) { return FormMissingRequestErrors.ContentInvalid; }
    }
    public static bool Valid(Guid? topicId, string? name, string? description) => topicId.HasValue
        ? topicId.Value != Guid.Empty && name is null && description is null
        : FormAssistantCapabilityCodec.Text(name, 120) && FormAssistantCapabilityCodec.Text(description, 500);

    public static Result<FormObservedMissingTopics> Find(IReadOnlyList<AssistantObservedRead> reads, Guid turnId, Guid recipient, string culture)
    {
        var matching = reads.Where(read => read.TurnId == turnId && read.Capability.Id == BusinessFormAssistantCapabilitySource.CandidatesId).ToArray();
        if (matching.Length == 0 || matching.Select(read => read.Ordinal).Distinct().Count() != matching.Length) return FormMissingRequestErrors.ContentInvalid;
        var latest = matching.MaxBy(read => read.Ordinal)!;
        if (latest.Ordinal < 1 || latest.Capability.Version != 1 || latest.Payload is not FormAssistantCandidatesPayload payload
            || !FormAssistantCapabilityCodec.ValidRead(payload) || payload.RecipientUserId != recipient || payload.Culture != culture)
            return FormMissingRequestErrors.ContentInvalid;
        return new FormObservedMissingTopics(latest.Ordinal, payload);
    }
}
internal sealed record FormObservedMissingTopics(int ReadOrdinal, FormAssistantCandidatesPayload Snapshot)
{ public override string ToString() => "FormObservedMissingTopics { Content = [REDACTED] }"; }
