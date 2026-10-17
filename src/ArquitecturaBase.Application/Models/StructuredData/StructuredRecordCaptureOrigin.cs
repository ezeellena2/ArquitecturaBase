using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredRecordSenderKind { Manual, Assistant }

public sealed record StructuredRecordCaptureOrigin(StructuredRecordSenderKind SenderKind, Guid? OperatorUserId,
    string? Reason, StructuredRecordAssistantProposal? Proposal, IReadOnlyList<StructuredRecordProposedValue> ProposedValues,
    IReadOnlyList<Guid> IncludedFieldIds, StructuredRecordSuggestionOrigin? Suggestion = null)
{
    public override string ToString() => "StructuredRecordCaptureOrigin { Content = [REDACTED] }";
}
public sealed record StructuredRecordAssistantProposal(Guid ProposalId, string CapabilityId, int CapabilityVersion,
    AssistantOperationProvenance Provenance)
{
    public override string ToString() => "StructuredRecordAssistantProposal { Content = [REDACTED] }";
}
public sealed record StructuredRecordProposedValue(Guid FieldId, string Type, string Value)
{
    public override string ToString() => "StructuredRecordProposedValue { Content = [REDACTED] }";
}
public sealed record StructuredRecordSuggestionOrigin(Guid OriginRecordId, Guid OriginSuggestionId, Guid SuggestionSetId,
    IReadOnlyList<StructuredRecordSuggestionMapping> Mappings)
{
    public override string ToString() => "StructuredRecordSuggestionOrigin { Content = [REDACTED] }";
}
public sealed record StructuredRecordSuggestionMapping(string SourceKind, Guid? SourceFieldId, Guid DestinationFieldId)
{
    public override string ToString() => "StructuredRecordSuggestionMapping { Content = [REDACTED] }";
}
