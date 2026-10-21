using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Resources;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// The dispatcher calls the owner outside its own UoW, then deduplicates the fixed acknowledgement in its existing outbox.
internal sealed class WhatsAppAssistantTerminalPolicy(IStructuredCatalogProposalSink catalogs,
    IStructuredMissingFormRequestSink missing, IPublicOrigin origin)
{
    public async Task<Result<WhatsAppAssistantTerminalResponse?>> ApplyAsync(WhatsAppAssistantReferenceRow reference,
        WhatsAppAssistantSourceRow source, AssistantOperationResponse operation, CancellationToken ct)
    {
        if (operation.State != "completed" || operation.Proposal is not {} proposal || operation.Provenance is not {} provenance
            || proposal.Capability is not ({ Id: "forms.catalog-filters.propose", Version: 1 } or { Id: "forms.missing-request.propose", Version: 1 }))
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
        string? body = null;
        if (proposal.Capability.Id == "forms.catalog-filters.propose")
        {
            var publicOrigin = origin.Value;
            if (automatic && publicOrigin is null) return WhatsAppAssistantErrors.Unavailable;
            var accepted = await catalogs.AcceptAsync(new StructuredCatalogProposalAcceptanceRequest(context, proposal, provenance), ct);
            if (accepted.IsFailure) return accepted.Error;
            if (automatic)
            {
                var link = new Uri(publicOrigin!, "catalogos/" + accepted.Value.CatalogId.ToString("D"));
                body = string.Format(CultureInfo.InvariantCulture, AssistantTerminalTexts.Get("CatalogPrepared", context.Culture), link.AbsoluteUri);
            }
        }
        else
        {
            var captured = await missing.CaptureAsync(new StructuredMissingFormRequestCaptureRequest(source.Id, source.OccurredAtUtc, context, proposal, provenance), ct);
            if (captured.IsFailure) return captured.Error;
            if (automatic) body = AssistantTerminalTexts.Get("MissingFormRequested", context.Culture);
        }
        return new WhatsAppAssistantTerminalResponse(proposal.Id, body, context.Culture);
    }
}
