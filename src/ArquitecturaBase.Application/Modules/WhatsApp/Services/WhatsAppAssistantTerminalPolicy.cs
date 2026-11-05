using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// The dispatcher calls the owner outside its own UoW, then deduplicates the fixed reply in its existing outbox. An authoring
// proposal (a form, a catalog, H4's people catalog or why nothing can be built) is only summarized here: its button creates it.
internal sealed class WhatsAppAssistantTerminalPolicy(IStructuredAuthoringProposalChannel authoring,
    IStructuredMissingFormRequestSink missing, IPublicOrigin origin)
{
    private static readonly string[] Ids = ["forms.missing-request.propose", "forms.catalog-filters.propose", "forms.authoring.propose-form",
        "forms.authoring.propose-catalog", "forms.authoring.decline"];

    public static bool Handles(AssistantCapabilityReference capability) => capability.Version == 1 && Ids.Contains(capability.Id, StringComparer.Ordinal);

    public async Task<Result<WhatsAppAssistantTerminalResponse?>> ApplyAsync(WhatsAppAssistantReferenceRow reference,
        WhatsAppAssistantSourceRow source, AssistantOperationResponse operation, CancellationToken ct)
    {
        if (operation.State != "completed" || operation.Proposal is not {} proposal || operation.Provenance is not {} provenance || !Handles(proposal.Capability))
            return Result.Success<WhatsAppAssistantTerminalResponse?>(null);
        if (operation.RequestId != reference.RequestId || source.Id != reference.SourceMessageId || source.ContactId != reference.ContactId)
            return WhatsAppAssistantErrors.InvalidProposal;
        var context = WhatsAppAssistantSuggestionPolicy.Context(reference);
        if (context.ActorUserId != reference.ActorUserId || context.SubjectUserId != reference.SubjectUserId
            || context.ChannelId != (reference.Kind == WhatsAppAssistantRequestKind.Automatic ? "whatsapp-assistant" : "whatsapp-manual")
            || context.InteractionMode != (reference.Kind == WhatsAppAssistantRequestKind.Automatic ? AssistantInteractionMode.Assistant : AssistantInteractionMode.Manual)
            || reference.Kind == WhatsAppAssistantRequestKind.Automatic && reference.ActorUserId != reference.SubjectUserId)
            return WhatsAppAssistantErrors.InvalidProposal;
        var automatic = reference.Kind == WhatsAppAssistantRequestKind.Automatic;
        if (proposal.Capability.Id != "forms.missing-request.propose")
        {
            if (!automatic) return new WhatsAppAssistantTerminalResponse(proposal.Id, null, context.Culture);
            var summary = authoring.Summarize(proposal, context.Culture);
            var reply = summary is null ? null : WhatsAppAssistantAuthoringText.Summary(reference.RequestId, proposal.Id, summary, context.Culture, origin.Value);
            return reply is null ? WhatsAppAssistantErrors.Unavailable : reply;
        }
        var captured = await missing.CaptureAsync(new StructuredMissingFormRequestCaptureRequest(source.Id, source.OccurredAtUtc, context, proposal, provenance), ct);
        if (captured.IsFailure) return captured.Error;
        return new WhatsAppAssistantTerminalResponse(proposal.Id, automatic ? AssistantTerminalTexts.Get("MissingFormRequested", context.Culture) : null, context.Culture);
    }
}
