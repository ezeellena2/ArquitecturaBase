using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Procesa los mensajes entrantes pendientes de un contacto en una unidad de trabajo.</summary>
public interface IWhatsAppInboundService
{
    Task<Result> ProcessContactAsync(Guid contactId, CancellationToken cancellationToken);
}
