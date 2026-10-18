using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Procesa los mensajes entrantes pendientes de un contacto en su propio límite (ExecuteInTransactionAsync con OnSuccess),
/// abierto antes de tomar la fila del contacto.
/// </summary>
public interface IWhatsAppInboundService
{
    // True means the contact lock was acquired, even when access messages were already processed.
    // False skips the whole pipeline when another worker owns the contact or it no longer exists.
    Task<Result<bool>> ProcessContactAsync(Guid contactId, CancellationToken cancellationToken);
}
