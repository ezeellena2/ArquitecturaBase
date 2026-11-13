using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;

internal sealed class WhatsAppAssistantTerminalFixture : IStructuredAuthoringProposalChannel, IStructuredMissingFormRequestSink, IPublicOrigin
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public Guid ContactId { get; } = Guid.NewGuid();
    public Guid SourceId { get; } = Guid.NewGuid();
    public Guid SubjectId { get; } = Guid.NewGuid();
    public Guid OperatorId { get; } = Guid.NewGuid();
    public Guid ProposalId { get; } = Guid.NewGuid();
    public string Culture { get; set; } = "es";
    public string CapabilityId { get; set; } = "forms.missing-request.propose";
    public int CapabilityVersion { get; set; } = 1;
    public WhatsAppAssistantRequestKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; } = new(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);
    public Uri? Value { get; set; } = new("https://business.example.test/");
    // What the owner summarizes; it never creates from a terminal, only from the button.
    public StructuredAuthoringSummary? Summary { get; set; } = new(StructuredAuthoringKind.Form, "Parte de lluvias · LLU", ["1. Milímetros · número"], 0);
    public int SummaryCalls { get; private set; }
    public StructuredMissingFormRequestCaptureRequest? MissingRequest { get; private set; }
    public int MissingCalls { get; private set; }
    public Result<StructuredMissingFormRequestCaptureResponse> MissingResult { get; set; } = Result.Success(new StructuredMissingFormRequestCaptureResponse(Guid.NewGuid(), Guid.NewGuid(), false));
    /// <summary>The references a question walks to know whether its conversation has a spreadsheet (IA, Stage 2).</summary>
    public FakeWhatsAppAssistantReader Reader { get; } = new();
    public WhatsAppAssistantTerminalPolicy Policy => new(this, this, this, Reader,
        new WhatsAppAssistantDocumentPolicy(new ArquitecturaBase.Application.Channels.UnavailableStructuredDocumentChannel(), null!, null!, null!, Reader, TimeProvider.System));
    public AssistantChannelContext Context => new(Kind == WhatsAppAssistantRequestKind.Manual ? "whatsapp-manual" : "whatsapp-assistant",
        Kind == WhatsAppAssistantRequestKind.Manual ? OperatorId : SubjectId, SubjectId, Culture, "America/Argentina/Buenos_Aires", new([], []), [new(CapabilityId, CapabilityVersion)], [new("assistant_proposal", 1)]) { InteractionMode = Kind == WhatsAppAssistantRequestKind.Manual ? AssistantInteractionMode.Manual : AssistantInteractionMode.Assistant };
    public WhatsAppAssistantReferenceRow Reference => new(RequestId, ContactId, SourceId, Context.ActorUserId, SubjectId, Kind, 0, 1,
        JsonSerializer.Serialize(Context, JsonSerializerOptions.Web), OccurredAtUtc.AddDays(90), WhatsAppAssistantRequestState.Pending, null, null, null);
    public WhatsAppAssistantSourceRow Source => new(SourceId, ContactId, "Never echo the original message", OccurredAtUtc, OccurredAtUtc.AddDays(90));
    public AssistantOperationResponse Operation => new(RequestId, "completed", new(Guid.NewGuid(), ProposalId, new(CapabilityId, CapabilityVersion), new TerminalPayload()),
        new("qualified-model", 3, "qualified-instructions", 7, OccurredAtUtc, Context.ChannelId), null);
    public StructuredAuthoringSummary? Summarize(Guid requestId, AssistantObservedProposal proposal, string culture) { SummaryCalls++; return Summary; }
    public Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException("A terminal never creates an authoring proposal.");
    public Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); MissingCalls++; MissingRequest = request; return Task.FromResult(MissingResult); }
    private sealed record TerminalPayload : AssistantCapabilityPayload;
}
