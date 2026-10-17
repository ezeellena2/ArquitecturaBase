using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record FormSuggestionAssistantSourceValue(Guid FieldId, string Type, string Label, string? Value)
{
    public override string ToString() => "FormSuggestionAssistantSourceValue { Content = [REDACTED] }";
}
public sealed record FormSuggestionAssistantCandidate(Guid SuggestionId, FormAssistantCandidate Form,
    IReadOnlyList<FormAssistantValue> MappedValues, bool CreatesAutomaticLoop)
{
    public override string ToString() => "FormSuggestionAssistantCandidate { Content = [REDACTED] }";
}
public sealed record FormSuggestionAssistantCandidatesPayload(string State, Guid RecipientUserId, string Culture, bool IsComplete,
    Guid SourceRecordId, Guid? SuggestionSetId, long SuggestionRevision, string SourceName, string SourceNumber,
    IReadOnlyList<FormSuggestionAssistantSourceValue> SourceValues, IReadOnlyList<FormSuggestionAssistantCandidate> Suggestions) : AssistantCapabilityPayload;
public sealed record FormSuggestionAssistantRegistrationPayload(string State, int ReadOrdinal, Guid SourceRecordId, Guid SuggestionSetId,
    long ExpectedSuggestionRevision, Guid SuggestionId, Guid DefinitionId, Guid VersionId, long ExpectedDefinitionRevision,
    IReadOnlyList<FormAssistantValue> Values, string Reason) : AssistantCapabilityPayload;
internal sealed record FormSuggestionAssistantReadArguments(Guid SourceRecordId)
{ public override string ToString() => "FormSuggestionAssistantReadArguments { Content = [REDACTED] }"; }
internal sealed record FormSuggestionAssistantRegistrationArguments(Guid SourceRecordId, Guid SuggestionId,
    IReadOnlyList<FormAssistantValue> Values, string Reason)
{ public override string ToString() => "FormSuggestionAssistantRegistrationArguments { Content = [REDACTED] }"; }
