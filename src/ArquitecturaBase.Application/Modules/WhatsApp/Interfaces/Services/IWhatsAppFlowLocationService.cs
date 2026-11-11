using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// W1-A: the locations and the «Seguir sin ubicación» taps of a contact resolve the sends that wait for them, and each resolved send
/// queues its Flow in the same boundary. Returns how many sends it resolved.
/// </summary>
public interface IWhatsAppFlowLocationService
{
    Task<Result<int>> ProcessAsync(Guid contactId, CancellationToken cancellationToken);
}
