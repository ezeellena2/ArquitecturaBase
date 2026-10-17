using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogProposalService
{
    Task<Result<FormCatalogSaveResponse>> ApproveAsync(Guid id, ApproveFormCatalogProposalRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogDetailResponse>> DiscardAsync(Guid id, DiscardFormCatalogProposalRequest request, CancellationToken cancellationToken);
}
