using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogImportService
{
    Task<Result<FormCatalogImportPreviewResponse>> PreviewAsync(Guid id, FormCatalogImportRequest request, CancellationToken cancellationToken);
    Task<Result<FormCatalogSaveResponse>> ApplyAsync(Guid id, FormCatalogImportRequest request, CancellationToken cancellationToken);
}
