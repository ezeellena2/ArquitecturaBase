using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
public interface IWhatsAppFormMessageDispatchService
{
    Task<Result<WhatsAppFlowMessageResponse>> PrepareAsync(Guid outgoingId, CancellationToken cancellationToken);
}
