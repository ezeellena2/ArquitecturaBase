namespace ArquitecturaBase.Application.Models.Assistant;

public sealed record AssistantAvailabilityResponse(string State);

public sealed record AssistantOperationRequest(Guid RequestId, AssistantChannelContext Context,
    string Text, DateTime OccurredAtUtc, DateTime ContentExpiresAtUtc)
{
    public AssistantAudioInput? Audio { get; init; }
    public override string ToString() => "AssistantOperationRequest { Content = [REDACTED] }";
}

public sealed record AssistantOperationAcceptedResponse(Guid RequestId, string State);

public sealed record AssistantOperationProvenance(string ModelProfileId, int ModelProfileVersion,
    string InstructionsId, int InstructionsVersion, DateTime OccurredAtUtc, string ChannelId);

public sealed record AssistantOperationResponse(Guid RequestId, string State, AssistantObservedProposal? Proposal,
    AssistantOperationProvenance? Provenance, string? FailureCode)
{
    public AssistantNormalizedAudio? NormalizedAudio { get; init; }
    public override string ToString() => "AssistantOperationResponse { Content = [REDACTED] }";
}
