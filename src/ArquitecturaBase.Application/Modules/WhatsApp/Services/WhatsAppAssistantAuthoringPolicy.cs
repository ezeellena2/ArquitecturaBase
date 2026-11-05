using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

// The owners' part of a tap on an authoring summary, outside any channel transaction: only the latest summary's buttons count,
// «Cambiar algo» waits for the change, and «Crear» reads the operation again and lets its owner create it once.
internal sealed class WhatsAppAssistantAuthoringPolicy(IWhatsAppAssistantReader reader, IAssistantOperations operations,
    IStructuredAuthoringProposalChannel authoring, IPublicOrigin origin)
{
    public async Task<WhatsAppAssistantAuthoringReply?> ReplyAsync(Guid contactId, WhatsAppAssistantChoiceRow tap, CancellationToken ct)
    {
        if (!WhatsAppAssistantAuthoringReplyCodec.TryParse(tap.ReplyId, out var requestId, out var proposalId, out var action)) return null;
        var reference = await reader.FindAsync(contactId, requestId, ct);
        if (reference is not { Kind: WhatsAppAssistantRequestKind.Automatic, State: WhatsAppAssistantRequestState.Sent } || reference.ProposalId != proposalId)
            return null;
        var culture = WhatsAppAssistantSuggestionPolicy.Context(reference).Culture;
        if (await reader.FindLatestAuthoringRequestAsync(contactId, ct) != requestId)
            return new(reference, WhatsAppAssistantAuthoringText.Text("AuthoringReplaced", culture), culture, null);
        if (action == WhatsAppAssistantAuthoringReplyCodec.Change)
            return new(reference, WhatsAppAssistantAuthoringText.Text("AuthoringChangePrompt", culture), culture, requestId);
        var context = WhatsAppAssistantSuggestionPolicy.Context(reference);
        var operation = await operations.GetAsync(context, requestId, ct);
        if (operation.IsFailure || operation.Value is not { Proposal: {} proposal, Provenance: {} provenance } || proposal.Id != proposalId
            || origin.Value is not {} publicOrigin)
            return new(reference, WhatsAppAssistantAuthoringText.Text("AuthoringNotCreated", culture), culture, null);
        var created = await authoring.CreateAsync(new(context, proposal, provenance), ct);
        return new(reference, created.IsSuccess ? WhatsAppAssistantAuthoringText.Created(created.Value, culture, publicOrigin)
            : WhatsAppAssistantAuthoringText.Text("AuthoringNotCreated", culture), culture, null);
    }
}

internal sealed record WhatsAppAssistantAuthoringReply(WhatsAppAssistantReferenceRow Reference, string Body, string Culture, Guid? ChangeRequestId)
{
    public override string ToString() => "WhatsAppAssistantAuthoringReply { Content = [REDACTED] }";
}
