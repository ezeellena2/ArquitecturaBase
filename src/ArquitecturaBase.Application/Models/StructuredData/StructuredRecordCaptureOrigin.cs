using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredRecordSenderKind { Manual, Assistant }

public sealed record StructuredRecordCaptureOrigin(StructuredRecordSenderKind SenderKind, Guid? OperatorUserId,
    string? Reason, StructuredRecordAssistantProposal? Proposal, IReadOnlyList<StructuredRecordProposedValue> ProposedValues,
    IReadOnlyList<Guid> IncludedFieldIds, StructuredRecordSuggestionOrigin? Suggestion = null)
{
    /// <summary>
    /// The received document the response came from (DOC5-A), an opaque id of its owner (<c>origin.document</c>, which «Cómo viene leyendo»
    /// counts); omitted otherwise, so prior origins keep their bytes.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? Document { get; init; }
    /// <summary>
    /// The document's revision the reviewer saw when passing it (DOC5-A), checked under its lock; null when its person confirms it in a
    /// channel. Never stored nor in the replay's fingerprint.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public long? DocumentRevision { get; init; }
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
