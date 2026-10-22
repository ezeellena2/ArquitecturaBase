using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;

public interface IFormCatalogExportService
{
    Task<Result<FormExportResponse>> ExportAsync(Guid id, bool template, CancellationToken cancellationToken);
}
