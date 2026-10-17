using System.Security.Cryptography;
using System.Text;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

internal sealed class WhatsAppFlowSessionVerifier(IWhatsAppFlowSessionProtector protector, TimeProvider timeProvider)
{
    public Result<WhatsAppFlowAssistantContent?> ReadAssistant(WhatsAppFormDispatchRow row)
    {
        if (row.AssistantContentExpiresAtUtc is null && row.ProtectedAssistantContent is not { Length: > 0 })
            return row.Origin == WhatsAppMessageOrigin.Conversation ? Result.Success<WhatsAppFlowAssistantContent?>(null) : FlowErrors.StateConflict;
        if (row.AssistantContentExpiresAtUtc is not {} expiresAtUtc || expiresAtUtc <= timeProvider.GetUtcNow().UtcDateTime
            || row.ProtectedAssistantContent is not { Length: > 0 } content) return FlowErrors.StateConflict;
        try
        {
            var decoded = protector.ReadAssistant(row.SessionId,row.ContactId,row.UserId,row.VersionId,content);
            var automatic = row.Origin == WhatsAppMessageOrigin.Assistant;
            return decoded.Format == 1 && decoded.Origin.OperatorUserId == row.OperatorUserId
                && decoded.Origin.SenderKind == (automatic ? ArquitecturaBase.Application.Models.StructuredData.StructuredRecordSenderKind.Assistant : ArquitecturaBase.Application.Models.StructuredData.StructuredRecordSenderKind.Manual)
                ? decoded : FlowErrors.StateConflict;
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException) { return FlowErrors.StateConflict; }
    }

    public Result<WhatsAppFlowSessionPayload> Verify(WhatsAppFormDispatchRow row)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (row.State != WhatsAppOutgoingMessageState.Sending || row.Body is null
            || row.Origin is not (WhatsAppMessageOrigin.Conversation or WhatsAppMessageOrigin.Assistant)
            || row.Mode != (row.Origin == WhatsAppMessageOrigin.Assistant ? WhatsAppConversationMode.Assistant : WhatsAppConversationMode.Manual)
            || row.LastInboundAtUtc <= now.AddHours(-24) || row.LastInboundAtUtc > now.AddMinutes(5)
            || row.ExpiresAtUtc <= now)
            return FlowErrors.StateConflict;

        WhatsAppFlowSessionPayload payload;
        try { payload = protector.Read(row.SessionId, row.ContactId, row.UserId, row.VersionId, row.ProtectedPayload); }
        catch (CryptographicException) { return FlowErrors.StateConflict; }
        catch (InvalidOperationException) { return FlowErrors.StateConflict; }
        if (payload.Origin != row.Origin || payload.Format != (row.Origin == WhatsAppMessageOrigin.Assistant ? 2 : 1)
            || payload.OperatorUserId != row.OperatorUserId || payload.DefinitionId != row.DefinitionId
            || payload.BindingId != row.BindingId || payload.Culture != row.Culture
            || payload.ModeRevision != row.ModeRevision || payload.MetaFlowId.Length == 0)
            return FlowErrors.StateConflict;

        var tokenBytes = Encoding.UTF8.GetBytes(payload.Token);
        var tokenHash = SHA256.HashData(tokenBytes);
        CryptographicOperations.ZeroMemory(tokenBytes);
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(tokenHash, Convert.FromHexString(row.TokenHash)))
                return FlowErrors.StateConflict;
        }
        catch (FormatException)
        {
            return FlowErrors.StateConflict;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenHash);
        }

        return payload;
    }
}
