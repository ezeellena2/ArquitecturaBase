using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantAgentReader
{
    /// <summary>The main agent as saved, or null before its first save.</summary>
    Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Probar: active accounts linked to a WhatsApp chat, by name when <paramref name="search"/> is given, by name, up to
    /// <paramref name="limit"/>. Only <paramref name="withEmail"/> searches and returns the email and lets it name an account without a
    /// name; without it, the chat's WhatsApp name or its number do, as in the inbox.
    /// </summary>
    Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, bool withEmail, int limit, CancellationToken cancellationToken);
}
