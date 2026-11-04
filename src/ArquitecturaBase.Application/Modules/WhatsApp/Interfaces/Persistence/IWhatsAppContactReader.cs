using ArquitecturaBase.Application.Modules.WhatsApp.Models;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>Lecturas de los contactos de WhatsApp, sin seguimiento.</summary>
public interface IWhatsAppContactReader
{
    /// <summary>El chat vinculado a la cuenta, si tiene uno, y si el número <paramref name="waId"/> ya tiene chat.</summary>
    Task<WhatsAppProfileChatRow> FindProfileChatAsync(Guid userId, string waId, CancellationToken cancellationToken);
}
