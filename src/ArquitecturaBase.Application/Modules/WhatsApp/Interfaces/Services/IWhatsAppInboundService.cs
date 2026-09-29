using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// Procesa los mensajes entrantes pendientes de un contacto en su propio límite (ExecuteInTransactionAsync con OnSuccess),
/// abierto antes de tomar la fila del contacto.
/// </summary>
public interface IWhatsAppInboundService
{
    Task<Result> ProcessContactAsync(Guid contactId, CancellationToken cancellationToken);
}
