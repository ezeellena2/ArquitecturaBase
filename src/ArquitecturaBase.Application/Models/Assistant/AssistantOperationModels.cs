using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Models.Assistant;

public sealed record AssistantAvailabilityResponse(string State);

public sealed record AssistantOperationRequest(Guid RequestId, AssistantChannelContext Context,
    string Text, DateTime OccurredAtUtc, DateTime ContentExpiresAtUtc)
{
    public AssistantAudioInput? Audio { get; init; }
    /// <summary>A document to read (ReadDocument), alone: never with text, an audio, a spreadsheet, a previous operation or tools.</summary>
    public AssistantDocumentInput? Document { get; init; }
    /// <summary>A spreadsheet that goes with an interpretation, never with an audio; it becomes the conversation's.</summary>
    public AssistantAttachmentInput? Attachment { get; init; }
    /// <summary>The operation this message continues, the latest of its channel conversation: the turn joins that engine conversation.</summary>
    public Guid? PreviousRequestId { get; init; }
    public override string ToString() => "AssistantOperationRequest { Content = [REDACTED] }";
}

public sealed record AssistantOperationAcceptedResponse(Guid RequestId, string State);

public sealed record AssistantOperationProvenance(string ModelProfileId, int ModelProfileVersion,
    string InstructionsId, int InstructionsVersion, DateTime OccurredAtUtc, string ChannelId);

/// <summary>IsCurrent is false once a later turn of the same conversation closed on a proposal of the same capability or of its group.</summary>
public sealed record AssistantProposalLookupResponse(AssistantChannelContext Context, AssistantObservedProposal Proposal,
    AssistantOperationProvenance Provenance, bool IsCurrent)
{
    /// <summary>The spreadsheet of the proposal's turn, its own or inherited; null in a turn without one.</summary>
    public AssistantAttachmentDescriptor? Attachment { get; init; }
    public override string ToString() => "AssistantProposalLookupResponse { Content = [REDACTED] }";
}

public sealed record AssistantOperationResponse(Guid RequestId, string State, AssistantObservedProposal? Proposal,
    AssistantOperationProvenance? Provenance, string? FailureCode)
{
    public AssistantNormalizedAudio? NormalizedAudio { get; init; }
    /// <summary>What a completed ReadDocument read, while its content lasts.</summary>
    public StructuredDocumentReading? DocumentReading { get; init; }
    public override string ToString() => "AssistantOperationResponse { Content = [REDACTED] }";
}
