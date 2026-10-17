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
        if (outgoing.Origin == WhatsAppMessageOrigin.FormAction)
        {
            if (await formActions.ValidateAsync(contact, outgoing.RecipientUserId!.Value, outgoing, ct) is { } error) return error;
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

    private async Task<bool> ValidateAssistantContentAsync(WhatsAppOutgoingMessage outgoing, WhatsAppAssistantReferenceRow reference, CancellationToken ct)
    {
        if (outgoing.Kind == WhatsAppOutgoingMessageKind.Text) return true;
        if (outgoing.Kind == WhatsAppOutgoingMessageKind.Flow)
        {
            var session = await sessions.FindDispatchAsync(outgoing.Id, ct);
            return session is not null && session.Origin == WhatsAppMessageOrigin.Assistant && session.OutgoingId == outgoing.Id
                && session.SessionId == outgoing.FlowSessionId && session.UserId == reference.SubjectUserId && session.OperatorUserId is null
                && session.ExpiresAtUtc > clock.GetUtcNow().UtcDateTime
                && session.AssistantContentExpiresAtUtc > clock.GetUtcNow().UtcDateTime
                && session.ProtectedAssistantContent is { Length: > 0 }
                && !await references.ExistsDeclinedAsync(reference.ContactId, session.DefinitionId, ct)
                && await forms.FindAcceptedSessionAsync(session.UserId, session.Culture, session.DefinitionId, session.VersionId, ct) is not null;
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
