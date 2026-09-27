using ArquitecturaBase.Application.Models.WhatsApp;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// Guarda un lote del webhook en su propia transacción (IUnitOfWork.ExecuteInTransactionAsync con OnSuccess). Es la
/// unidad que corre el webhook y que IWhatsAppWebhookRetry vuelve a correr, en un scope nuevo, después de un 23505.
/// </summary>
public interface IWhatsAppWebhookPersistence
{
    Task PersistAsync(WhatsAppWebhookBatch batch, CancellationToken cancellationToken);
}
