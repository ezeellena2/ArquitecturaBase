using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;

/// <summary>
/// Lecturas de los mensajes guardados para mostrar, sin seguimiento. Para los locks y lo que registra el webhook,
/// <see cref="IWhatsAppMessageRepository"/>.
/// </summary>
public interface IWhatsAppMessageReader
{
    /// <summary>
    /// El último estado de entrega que avisó Meta del mensaje saliente <paramref name="waMessageId"/>; null si no hay un
    /// saliente guardado con ese id o Meta todavía no avisó nada. Un entrante con el mismo id no cuenta.
    /// </summary>
    Task<WhatsAppMessageStatus?> FindOutboundStatusAsync(string waMessageId, CancellationToken cancellationToken);
}
