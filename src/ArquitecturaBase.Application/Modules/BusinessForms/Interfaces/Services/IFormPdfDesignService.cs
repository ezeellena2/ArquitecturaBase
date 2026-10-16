using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormPdfDesignService
{
    Task<Result<FormPdfDesignResponse>> GetCurrentAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<Result<FormPdfDesignResponse>> SaveAsync(Guid definitionId, SaveFormPdfDesignRequest request, CancellationToken cancellationToken);
    Task<Result<FormPdfContentResponse>> PreviewAsync(Guid definitionId, PreviewFormPdfDesignRequest request, CancellationToken cancellationToken);
}
