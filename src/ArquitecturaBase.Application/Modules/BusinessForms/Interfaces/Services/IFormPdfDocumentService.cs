using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormPdfDocumentService
{
    Task<Result<FormPdfContentResponse>> GetAsync(Guid recordId, Guid documentId, CancellationToken cancellationToken);
}
