using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowSessionIssuer(IWhatsAppFlowSessionProtector protector, TimeProvider timeProvider)
{
    public WhatsAppFlowSessionIssue Create(WhatsAppFormSendContext context, SendWhatsAppFormRequest request,
        Guid actor, string fingerprint, string token, IReadOnlyList<StructuredFormCatalogOffer>? catalogs = null) =>
        Issue(context, request, actor, fingerprint, token, catalogs, null, null);

    public WhatsAppFlowSessionIssue CreateProposed(WhatsAppFormSendContext context, Guid requestId, long modeRevision,
        Guid? actor, string fingerprint, string token, WhatsAppFlowAssistantContent content, DateTime sourceExpiresAtUtc,
        IReadOnlyList<StructuredFormCatalogOffer>? catalogs = null) =>
        Issue(context, new(context.Recipient.ContactId, requestId, context.Form.DefinitionId, context.Form.VersionId,
            modeRevision, context.RecipientRevision, null), actor, fingerprint, token, catalogs, content, sourceExpiresAtUtc);

    private WhatsAppFlowSessionIssue Issue(WhatsAppFormSendContext context, SendWhatsAppFormRequest request, Guid? actor,
        string fingerprint, string token, IReadOnlyList<StructuredFormCatalogOffer>? catalogs,
        WhatsAppFlowAssistantContent? content, DateTime? sourceExpiresAtUtc)
    {
        var recipient = context.Recipient;
        var userId = recipient.UserId!.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var automatic = actor is null;
        if (automatic && content is null || content is not null && (content.Format != 1
            || content.Origin.OperatorUserId != actor
            || content.Origin.SenderKind != (automatic ? StructuredRecordSenderKind.Assistant : StructuredRecordSenderKind.Manual)))
            throw new InvalidOperationException("The proposed session must preserve its explicit sender identity.");
        var culture = content is null ? recipient.Culture! : context.Projection.Culture;
        if (culture != context.Binding.Culture) throw new InvalidOperationException("The session presentation must match its published binding.");
        var expiresAtUtc = nowUtc.Add(context.SessionLifetime);
        if (sourceExpiresAtUtc is {} sourceExpiry)
        {
            if (sourceExpiry.Kind != DateTimeKind.Utc || sourceExpiry <= nowUtc)
                throw new InvalidOperationException("The proposed content must have an unexpired UTC retention limit.");
            if (sourceExpiry < expiresAtUtc) expiresAtUtc = sourceExpiry;
        }
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        string tokenHash;
        try { tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes)); }
        finally { CryptographicOperations.ZeroMemory(tokenBytes); }
        var nameJson = JsonSerializer.Serialize(context.Form.Name);
        var session = actor is {} sender
            ? WhatsAppFlowSession.Create(request.ContactId, recipient.LinkRevision, userId, sender, request.DefinitionId,
                request.VersionId, checked((int)context.Form.VersionNumber), context.Binding.BindingId, culture, nameJson,
                context.Form.Prefix, tokenHash, fingerprint, expiresAtUtc)
            : WhatsAppFlowSession.CreateAssistant(request.ContactId, recipient.LinkRevision, userId, request.DefinitionId,
                request.VersionId, checked((int)context.Form.VersionNumber), context.Binding.BindingId, culture, nameJson,
                context.Form.Prefix, tokenHash, fingerprint, expiresAtUtc);
        var payload = new WhatsAppFlowSessionPayload(automatic ? 2 : 1, request.ContactId, userId, actor, request.DefinitionId,
            request.VersionId, context.Binding.BindingId, culture, context.RecipientRevision, request.ExpectedModeRevision,
            context.Binding.MetaFlowId, context.Binding.TechnicalName, context.Projection.Hash, token, context.ButtonText,
            context.StartScreen, context.Account, catalogs, automatic ? WhatsAppMessageOrigin.Assistant : WhatsAppMessageOrigin.Conversation);
        session.SealPayload(protector.Protect(session.Id, request.ContactId, userId, request.VersionId, payload));
        if (content is not null && !session.SealAssistantContent(
            protector.ProtectAssistant(session.Id, request.ContactId, userId, request.VersionId, content), sourceExpiresAtUtc!.Value))
            throw new InvalidOperationException("The proposed session content must be sealed exactly once.");
        return new(session, nowUtc);
    }
}

internal sealed record WhatsAppFlowSessionIssue(WhatsAppFlowSession Session, DateTime CreatedAtUtc);
