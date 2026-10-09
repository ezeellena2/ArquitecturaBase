using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Api.Modules.WhatsApp.Contracts;

public sealed record ListWhatsAppChatsQuery(int? Page, int? PageSize, string? Sort, string? Search, bool UnreadOnly)
{
    public override string ToString() => nameof(ListWhatsAppChatsQuery);
}
public sealed record ListWhatsAppMessagesQuery(DateTime? BeforeAtUtc, Guid? BeforeId, int Limit = 50);
public sealed record SendWhatsAppTextHttpRequest(Guid RequestId, string? Body)
{
    public override string ToString() => nameof(SendWhatsAppTextHttpRequest);
}
public sealed record ChangeWhatsAppModeHttpRequest(WhatsAppConversationMode Mode, long ExpectedRevision);
public sealed record MarkWhatsAppChatReadHttpRequest(long ThroughSequence);
