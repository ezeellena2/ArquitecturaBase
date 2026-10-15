using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormFileService
{
    Task<Result<FormFileContentResponse>> OpenAsync(Guid recordId, Guid fileId, bool thumbnail, bool download, CancellationToken cancellationToken);
}
