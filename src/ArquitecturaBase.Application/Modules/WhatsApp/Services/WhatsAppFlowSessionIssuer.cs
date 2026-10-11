using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowSessionIssuer(IWhatsAppFlowSessionProtector protector, TimeProvider timeProvider)
{
    public WhatsAppFlowSessionIssue Create(WhatsAppFormSendContext context, SendWhatsAppFormRequest request,
        Guid actor, string fingerprint, string token)
    {
        var recipient = context.Recipient;
        var userId = recipient.UserId!.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var tokenBytes = System.Text.Encoding.UTF8.GetBytes(token);
        var tokenHash = Convert.ToHexStringLower(SHA256.HashData(tokenBytes));
        CryptographicOperations.ZeroMemory(tokenBytes);
        var session = WhatsAppFlowSession.Create(request.ContactId, userId, actor, request.DefinitionId,
            request.VersionId, checked((int)context.Form.VersionNumber), context.Binding.BindingId, recipient.Culture!,
            JsonSerializer.Serialize(context.Form.Name), tokenHash, fingerprint, nowUtc.AddMinutes(30));
        var payload = new WhatsAppFlowSessionPayload(1, request.ContactId, userId, actor, request.DefinitionId,
            request.VersionId, context.Binding.BindingId, recipient.Culture!, context.RecipientRevision,
            request.ExpectedModeRevision, context.Binding.MetaFlowId, context.Binding.TechnicalName, context.Projection.Hash,
            token, context.ButtonText, context.StartScreen, context.Account);
        session.SealPayload(protector.Protect(session.Id, request.ContactId, userId, request.VersionId, payload));
        return new(session, nowUtc);
    }
}

internal sealed record WhatsAppFlowSessionIssue(WhatsAppFlowSession Session, DateTime CreatedAtUtc);
