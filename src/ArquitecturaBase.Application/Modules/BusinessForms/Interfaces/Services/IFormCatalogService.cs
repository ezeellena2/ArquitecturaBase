using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogService
{
    Task<Result<FormCatalogsPageResponse>> ListAsync(ListFormCatalogsRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogSaveResponse>> CreateManualAsync(CreateManualCatalogRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogSaveResponse>> UpdateManualAsync(Guid id, UpdateManualCatalogRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<Result<PagedResult<FormCatalogItemResponse>>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogPresentationCheckResponse>> CheckPresentationAsync(Guid id, FormCatalogPresentationCheckRequest request, CancellationToken cancellationToken);
}
