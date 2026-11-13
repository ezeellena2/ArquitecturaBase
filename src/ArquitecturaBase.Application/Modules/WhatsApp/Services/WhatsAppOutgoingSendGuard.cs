using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppOutgoingSendGuard(IPhoneNumberParser phoneNumbers, WhatsAppManualSendGuard manualSends,
    WhatsAppFormActionPolicy formActions, WhatsAppAssistantAccessPolicy assistants, IWhatsAppAssistantReader references,
    IWhatsAppFlowSessionReader sessions, IStructuredFormSource forms, TimeProvider clock)
{
    public async Task<Result<PhoneNumber>> ValidateAsync(WhatsAppContact? contact, WhatsAppOutgoingMessage outgoing, CancellationToken ct)
    {
        // W3: a resend goes out only while its send has no reply, captured or attached by the reply worker, since the first reply is
        // the one kept (A7); then, as any message of its sender, below.
        if (outgoing.IsResend && await sessions.FindDispatchAsync(outgoing.Id, ct) is not { CaptureState: WhatsAppFlowCaptureState.Pending, HasReply: false })
            return FlowErrors.StateConflict;
        if (outgoing.Origin == WhatsAppMessageOrigin.FormAction)
        {
            if (await formActions.ValidateAsync(contact, outgoing.RecipientUserId!.Value, outgoing, ct) is { } error) return error;
        }
        else if (outgoing.Origin == WhatsAppMessageOrigin.Assistant && outgoing.IsAssistantHandoff)
        {
            // Derivar (AS4): the fixed message goes out after the conversation passed to Atención manual, and only while it still
            // needs a person with the same mode revision; going back to the assistant or another handoff leaves it unsent.
            if (contact is null || contact.Mode != WhatsAppConversationMode.Manual || !contact.NeedsPerson || contact.ModeRevision != outgoing.AssistantModeRevision
                || contact.LinkRevision != outgoing.RecipientLinkRevision || contact.WaId != outgoing.RecipientWaId
                || contact.UserId != outgoing.RecipientUserId) return WhatsAppAssistantErrors.StateConflict;
            if (await formActions.ValidateAsync(contact, outgoing.RecipientUserId!.Value, null, ct) is { } error) return error;
        }
        else if (outgoing.Origin == WhatsAppMessageOrigin.Assistant)
        {
            if (contact is null || contact.Mode != WhatsAppConversationMode.Assistant || contact.ModeRevision != outgoing.AssistantModeRevision
                || contact.LinkRevision != outgoing.RecipientLinkRevision || contact.WaId != outgoing.RecipientWaId
                || contact.UserId != outgoing.RecipientUserId) return WhatsAppAssistantErrors.StateConflict;
            var reference = await references.FindByOutgoingAsync(outgoing.Id, ct);
            if (reference is null || reference.Kind != WhatsAppAssistantRequestKind.Automatic
                || reference.ActorUserId != reference.SubjectUserId || reference.SubjectUserId != outgoing.RecipientUserId)
                return WhatsAppAssistantErrors.StateConflict;
            if (await assistants.RevalidateAsync(contact, reference, ct) is { } error) return error;
            if (!await ValidateAssistantContentAsync(outgoing, reference, ct)) return WhatsAppAssistantErrors.InvalidProposal;
        }
        else if (contact is null || contact.Mode != WhatsAppConversationMode.Manual
            || !WhatsAppChatService.IsReplyWindowOpen(contact, clock.GetUtcNow().UtcDateTime)
            || !await manualSends.IsAllowedAsync(outgoing.OperatorUserId, ct)) return WhatsAppChatErrors.ManualRequired;
        if (string.IsNullOrWhiteSpace(outgoing.Body)) return WhatsAppAssistantErrors.InvalidRequest;
        return WhatsAppIds.Parse(phoneNumbers, contact?.WaId);
    }

    private static bool IsLocationSkip(WhatsAppOutgoingMessage outgoing) =>
        outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies && outgoing.FlowSessionId is { } sessionId
        && outgoing.QuickReplies is [var reply] && reply.Id == WhatsAppFlowLocationReply.Id(sessionId);

    private async Task<bool> ValidateAssistantContentAsync(WhatsAppOutgoingMessage outgoing, WhatsAppAssistantReferenceRow reference, CancellationToken ct)
    {
        if (outgoing.Kind == WhatsAppOutgoingMessageKind.Text) return true;
        // Task 9 of the documents: a document question is the outgoing its own reference accepted, every option of its operation and proposal.
        if (outgoing.Kind is WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List && outgoing.QuickReplies.Count > 0
            && outgoing.QuickReplies.All(reply => reply.Id.StartsWith(WhatsAppAssistantDocumentReplyCodec.Prefix, StringComparison.Ordinal)))
            return outgoing.Id == reference.OutgoingId && reference.ProposalId is { } documentProposal && outgoing.QuickReplies.All(reply =>
                WhatsAppAssistantDocumentReplyCodec.TryParse(reply.Id, out var requestId, out var proposal, out _)
                    && requestId == reference.RequestId && proposal == documentProposal);
        // W1-A: a send of a form with a location opens with its request and, when optional, «Seguir sin ubicación»; they are checked
        // like its Flow, which goes out later with another RequestId, and none of them goes out once the content has expired.
        if (outgoing.Kind is WhatsAppOutgoingMessageKind.Flow or WhatsAppOutgoingMessageKind.LocationRequest || IsLocationSkip(outgoing))
        {
            var session = await sessions.FindDispatchAsync(outgoing.Id, ct);
            return session is not null && session.Origin == WhatsAppMessageOrigin.Assistant && session.OutgoingId == outgoing.Id
                && session.SessionId == outgoing.FlowSessionId && session.UserId == reference.SubjectUserId && session.OperatorUserId is null
                && session.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime
                && session.AssistantContentExpiresAtUtc > clock.GetUtcNow().UtcDateTime
                && session.ProtectedAssistantContent is { Length: > 0 }
                // Review of task 9: a form that answers a file the person sent (its reference carries it) is no offer, so a decline does not stop it.
                && (reference.AttachmentMessageId is not null || !await references.ExistsDeclinedAsync(reference.ContactId, session.DefinitionId, ct))
                && await forms.FindAcceptedSessionAsync(session.UserId, session.Culture, session.DefinitionId, session.VersionId, ct) is not null;
        }
        // A case's question («Preguntar con botones») is, like an authoring summary, the outgoing its own reference accepted, so it is
        // told apart by its ids: every button belongs to that reference and keeps its place, which also bounds them to three.
        if (outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies
            && outgoing.QuickReplies.Any(reply => reply.Id.StartsWith(WhatsAppAssistantCaseButtonCodec.Prefix, StringComparison.Ordinal)))
            return outgoing.Id == reference.OutgoingId && outgoing.QuickReplies.Select((reply, place) =>
                WhatsAppAssistantCaseButtonCodec.TryParse(reply.Id, out var requestId, out var index)
                    && requestId == reference.RequestId && index == place).All(valid => valid);
        // IA3-A: an authoring summary carries exactly «Cambiar algo» of its own operation and proposal, with «Crear» or, in an import's
        // summary (IA, Stage 2), «Revisar importación».
        if (outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies && outgoing.Id == reference.OutgoingId)
        {
            if (reference.ProposalId is not {} proposalId) return false;
            string?[] actions = [.. outgoing.QuickReplies.Select(reply =>
                WhatsAppAssistantAuthoringReplyCodec.TryParse(reply.Id, out var requestId, out var proposal, out var action)
                    && requestId == reference.RequestId && proposal == proposalId ? action : null).Order(StringComparer.Ordinal)];
            return actions.SequenceEqual([WhatsAppAssistantAuthoringReplyCodec.Change, WhatsAppAssistantAuthoringReplyCodec.Create])
                || actions.SequenceEqual([WhatsAppAssistantAuthoringReplyCodec.Change, WhatsAppAssistantAuthoringReplyCodec.Review]);
        }
        if (outgoing.Kind != WhatsAppOutgoingMessageKind.QuickReplies || outgoing.QuickReplies.Count is < 2 or > 3 || reference.ContextJson is not {} savedContext) return false;
        AssistantChannelContext? context;
        try { context = JsonSerializer.Deserialize<AssistantChannelContext>(savedContext, JsonSerializerOptions.Web); }
        catch (JsonException) { return false; }
        if (context is null) return false;
        foreach (var reply in outgoing.QuickReplies)
        {
            if (!WhatsAppAssistantReplyCodec.TryParse(reply.Id, out var requestId, out var definitionId, out var versionId)
                || requestId != reference.RequestId || await references.ExistsDeclinedAsync(reference.ContactId, definitionId, ct)
                || await forms.FindAcceptedSessionAsync(reference.SubjectUserId, context.Culture, definitionId, versionId, ct) is not { } form
                || form.DefinitionRevision != reply.DefinitionRevision) return false;
        }
        return true;
    }
}
