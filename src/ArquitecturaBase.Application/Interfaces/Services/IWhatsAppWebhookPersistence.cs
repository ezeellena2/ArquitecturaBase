using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Persists one parsed webhook batch in the current unit of work.</summary>
public interface IWhatsAppWebhookPersistence
{
    Task PersistAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken);
}
