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

internal sealed class WhatsAppAssistantTerminalFixture : IStructuredCatalogProposalSink, IStructuredMissingFormRequestSink, IPublicOrigin
{
    public Guid RequestId { get; } = Guid.NewGuid();
    public Guid ContactId { get; } = Guid.NewGuid();
    public Guid SourceId { get; } = Guid.NewGuid();
    public Guid SubjectId { get; } = Guid.NewGuid();
    public Guid OperatorId { get; } = Guid.NewGuid();
    public Guid ProposalId { get; } = Guid.NewGuid();
    public string Culture { get; set; } = "es";
    public string CapabilityId { get; set; } = "forms.missing-request.propose";
    public WhatsAppAssistantRequestKind Kind { get; set; }
    public DateTime OccurredAtUtc { get; } = new(2026, 10, 17, 15, 0, 0, DateTimeKind.Utc);
    public Uri? Value { get; set; } = new("https://business.example.test/");
    public StructuredCatalogProposalAcceptanceRequest? CatalogRequest { get; private set; }
    public StructuredMissingFormRequestCaptureRequest? MissingRequest { get; private set; }
    public int CatalogCalls { get; private set; }
    public int MissingCalls { get; private set; }
    public Result<StructuredCatalogProposalAcceptedResponse> CatalogResult { get; set; } = Result.Success(new StructuredCatalogProposalAcceptedResponse(Guid.NewGuid(), "Pending", "Never echo the model name", "es", 5, []));
    public Result<StructuredMissingFormRequestCaptureResponse> MissingResult { get; set; } = Result.Success(new StructuredMissingFormRequestCaptureResponse(Guid.NewGuid(), Guid.NewGuid(), false));
    public WhatsAppAssistantTerminalPolicy Policy => new(this, this, this);
    public AssistantChannelContext Context => new(Kind == WhatsAppAssistantRequestKind.Manual ? "whatsapp-manual" : "whatsapp-assistant",
        Kind == WhatsAppAssistantRequestKind.Manual ? OperatorId : SubjectId, SubjectId, Culture, "America/Argentina/Buenos_Aires", new([], []), [new(CapabilityId, 1)], [new("assistant_proposal", 1)]);
    public WhatsAppAssistantReferenceRow Reference => new(RequestId, ContactId, SourceId, Context.ActorUserId, SubjectId, Kind, 0, 1,
        JsonSerializer.Serialize(Context, JsonSerializerOptions.Web), OccurredAtUtc.AddDays(90), WhatsAppAssistantRequestState.Pending, null, null, null);
    public WhatsAppAssistantSourceRow Source => new(SourceId, ContactId, "Never echo the original message", OccurredAtUtc, OccurredAtUtc.AddDays(90));
    public AssistantOperationResponse Operation => new(RequestId, "completed", new(Guid.NewGuid(), ProposalId, new(CapabilityId, 1), new TerminalPayload()),
        new("qualified-model", 3, "qualified-instructions", 7, OccurredAtUtc, Context.ChannelId), null);
    public Task<Result<StructuredCatalogProposalAcceptedResponse>> AcceptAsync(StructuredCatalogProposalAcceptanceRequest request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); CatalogCalls++; CatalogRequest = request; return Task.FromResult(CatalogResult); }
    public Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); MissingCalls++; MissingRequest = request; return Task.FromResult(MissingResult); }
    private sealed record TerminalPayload : AssistantCapabilityPayload;
}
