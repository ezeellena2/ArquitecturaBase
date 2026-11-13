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
        Issue(context, request, actor, fingerprint, token, catalogs, null, null, null);

    /// <summary>
    /// A send proposed by the assistant. A received document's send (task 9 of the documents) expires when the inbox stops waiting for
    /// the person (<paramref name="expiresAtUtc"/>), never later than its own lifetime allows.
    /// </summary>
    public WhatsAppFlowSessionIssue CreateProposed(WhatsAppFormSendContext context, Guid requestId, long modeRevision,
        Guid? actor, string fingerprint, string token, WhatsAppFlowAssistantContent content, DateTime sourceExpiresAtUtc,
        IReadOnlyList<StructuredFormCatalogOffer>? catalogs = null, DateTime? expiresAtUtc = null) =>
        Issue(context, new(context.Recipient.ContactId, requestId, context.Form.DefinitionId, context.Form.VersionId,
            modeRevision, context.RecipientRevision, null), actor, fingerprint, token, catalogs, content, sourceExpiresAtUtc, expiresAtUtc);

    /// <summary>
    /// Task 9c of the documents: a new send of the form of a send whose reply a fixed validation rejected. It keeps the recipient, sender,
    /// form, version, binding, culture, request, deadline and resolved location of the rejected one and its sealed payload, with a token of
    /// its own (a send's capture is terminal and its owner keeps one receipt per send), and seals the content it opens with. Meta is not
    /// called here: the dispatch checks the account again.
    /// </summary>
    public WhatsAppFlowSession Again(WhatsAppFlowSession rejected, WhatsAppFlowSessionPayload payload, WhatsAppFlowAssistantContent content,
        DateTime contentExpiresAtUtc)
    {
        var automatic = rejected.Origin == WhatsAppMessageOrigin.Assistant;
        if (content.Format != 1 || content.Origin.OperatorUserId != rejected.OperatorUserId || payload.Origin != rejected.Origin
            || payload.OperatorUserId != rejected.OperatorUserId || payload.ContactId != rejected.ContactId || payload.VersionId != rejected.VersionId
            || content.Origin.SenderKind != (automatic ? StructuredRecordSenderKind.Assistant : StructuredRecordSenderKind.Manual))
            throw new InvalidOperationException("The new send must preserve its explicit sender identity.");
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        string tokenHash;
        try { tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes)); }
        finally { CryptographicOperations.ZeroMemory(tokenBytes); }
        var session = rejected.OperatorUserId is { } sender
            ? WhatsAppFlowSession.Create(rejected.ContactId, rejected.LinkRevision, rejected.UserId, sender, rejected.DefinitionId, rejected.VersionId,
                rejected.VersionNumber, rejected.BindingId, rejected.Culture, rejected.NameJson, rejected.Prefix, tokenHash, rejected.RequestFingerprint,
                rejected.ExpiresAtUtc)
            : WhatsAppFlowSession.CreateAssistant(rejected.ContactId, rejected.LinkRevision, rejected.UserId, rejected.DefinitionId, rejected.VersionId,
                rejected.VersionNumber, rejected.BindingId, rejected.Culture, rejected.NameJson, rejected.Prefix, tokenHash, rejected.RequestFingerprint,
                rejected.ExpiresAtUtc);
        session.ContinueLocation(rejected);
        session.SealPayload(protector.Protect(session.Id, rejected.ContactId, rejected.UserId, rejected.VersionId, payload with { Token = token }));
        if (!session.SealAssistantContent(protector.ProtectAssistant(session.Id, rejected.ContactId, rejected.UserId, rejected.VersionId, content),
            contentExpiresAtUtc))
            throw new InvalidOperationException("The content of a new send must be sealed exactly once.");
        return session;
    }

    private WhatsAppFlowSessionIssue Issue(WhatsAppFormSendContext context, SendWhatsAppFormRequest request, Guid? actor,
        string fingerprint, string token, IReadOnlyList<StructuredFormCatalogOffer>? catalogs,
        WhatsAppFlowAssistantContent? content, DateTime? sourceExpiresAtUtc, DateTime? fixedExpiresAtUtc)
    {
        var recipient = context.Recipient;
        var userId = recipient.UserId!.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var automatic = actor is null;
        if (automatic && content is null || content is not null && (content.Format != 1
            || content.Origin.OperatorUserId != actor
            || content.Origin.SenderKind != (automatic ? StructuredRecordSenderKind.Assistant : StructuredRecordSenderKind.Manual)))
            throw new InvalidOperationException("The proposed session must preserve its explicit sender identity.");
        var culture = context.Projection.Culture;
        if (culture != context.Binding.Culture) throw new InvalidOperationException("The session presentation must match its published binding.");
        var expiresAtUtc = nowUtc.Add(context.SessionLifetime);
        if (sourceExpiresAtUtc is {} sourceExpiry)
        {
            if (sourceExpiry.Kind != DateTimeKind.Utc || sourceExpiry <= nowUtc)
                throw new InvalidOperationException("The proposed content must have an unexpired UTC retention limit.");
            if (sourceExpiry < expiresAtUtc) expiresAtUtc = sourceExpiry;
        }
        if (fixedExpiresAtUtc is { } fixedExpiry)
        {
            if (fixedExpiry.Kind != DateTimeKind.Utc || fixedExpiry <= nowUtc)
                throw new InvalidOperationException("A fixed session expiry must be a future UTC instant.");
            if (fixedExpiry < expiresAtUtc) expiresAtUtc = fixedExpiry;
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
