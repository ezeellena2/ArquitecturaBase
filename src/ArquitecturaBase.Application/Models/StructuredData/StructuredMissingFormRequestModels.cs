using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Models.StructuredData;
public sealed record StructuredMissingFormRequestCaptureRequest(Guid SourceReceiptId, DateTime RequestedAtUtc,
    AssistantChannelContext Context, AssistantObservedProposal Proposal, AssistantOperationProvenance Provenance)
{ public override string ToString() => "StructuredMissingFormRequestCaptureRequest { Content = [REDACTED] }"; }
public sealed record StructuredMissingFormRequestCaptureResponse(Guid RequestId, Guid TopicId, bool WasAlreadyRecorded)
{ public override string ToString() => "StructuredMissingFormRequestCaptureResponse { Identity = [REDACTED] }"; }
