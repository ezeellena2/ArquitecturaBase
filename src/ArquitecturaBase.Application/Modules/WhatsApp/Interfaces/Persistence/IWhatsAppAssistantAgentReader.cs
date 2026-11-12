using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

public interface IWhatsAppAssistantAgentReader
{
    /// <summary>The main agent as saved, or null before its first save.</summary>
    Task<WhatsAppAssistantAgentRow?> FindMainAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Probar: active accounts linked to a WhatsApp chat, by name or email when <paramref name="search"/> is given, by name, up to
    /// <paramref name="limit"/>.
    /// </summary>
    Task<IReadOnlyList<WhatsAppAssistantTestRecipientRow>> ListTestRecipientsAsync(string? search, int limit, CancellationToken cancellationToken);
}
