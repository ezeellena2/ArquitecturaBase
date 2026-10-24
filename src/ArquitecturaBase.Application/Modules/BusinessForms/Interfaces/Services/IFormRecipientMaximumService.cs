using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormRecipientMaximumService
{
    Task<Result<FormRecipientMaximumResponse>> FindMaximumAsync(FormRecipientMaximumRequest request, CancellationToken cancellationToken);
}
