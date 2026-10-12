using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogFiltersService
{
    Task<Result<FormCatalogFilterOptionsResponse>> GetOptionsAsync(
        FormCatalogFilterOptionsRequest request, CancellationToken cancellationToken);

    Task<Result<FormCatalogFilterPreviewResponse>> PreviewAsync(
        FormCatalogFilterPreviewRequest request, CancellationToken cancellationToken);

    Task<Result<FormCatalogSaveResponse>> CreateAsync(
        CreateFiltersCatalogRequest request, CancellationToken cancellationToken);

    Task<Result<FormCatalogSaveResponse>> UpdateAsync(
        Guid id, UpdateFiltersCatalogRequest request, CancellationToken cancellationToken);
}
