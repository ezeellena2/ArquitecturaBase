using ArquitecturaBase.Application.Models.Assistant;
namespace ArquitecturaBase.Application.Models.StructuredData;
public sealed record StructuredMissingFormRequestCaptureRequest(Guid SourceReceiptId, DateTime RequestedAtUtc,
    AssistantChannelContext Context, AssistantObservedProposal Proposal, AssistantOperationProvenance Provenance)
{ public override string ToString() => "StructuredMissingFormRequestCaptureRequest { Content = [REDACTED] }"; }
public sealed record StructuredMissingFormRequestCaptureResponse(Guid RequestId, Guid TopicId, bool WasAlreadyRecorded)
{
    /// <summary>
    /// O55: the request names a form that exists and is completed only in the app. Nothing was recorded (both ids are empty) and the
    /// channel says where it is.
    /// </summary>
    public StructuredAppOnlyForm? AppOnlyForm { get; init; }
    public static StructuredMissingFormRequestCaptureResponse ForAppOnly(StructuredAppOnlyForm form) => new(Guid.Empty, Guid.Empty, false) { AppOnlyForm = form };
    public override string ToString() => "StructuredMissingFormRequestCaptureResponse { Identity = [REDACTED] }";
}
/// <summary>O55: a form completed only in the app, with its name in the person's language and its path in the app, relative to its origin.</summary>
public sealed record StructuredAppOnlyForm(Guid DefinitionId, string Name, string AppPath)
{ public override string ToString() => "StructuredAppOnlyForm { Content = [REDACTED] }"; }
