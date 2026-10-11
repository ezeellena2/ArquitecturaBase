using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

public interface IWhatsAppFormService
{
    Task<Result<WhatsAppFormsResponse>> ListAsync(ListWhatsAppFormsRequest request, CancellationToken cancellationToken);
    Task<WhatsAppFormContactResponse?> FindContactAsync(Guid contactId, CancellationToken cancellationToken);
}
