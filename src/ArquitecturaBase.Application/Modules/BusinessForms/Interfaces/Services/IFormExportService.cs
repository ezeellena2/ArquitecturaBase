using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormExportService
{
    Task<Result<FormExportResponse>> ExportAsync(Guid id, FormExportRequest request, CancellationToken cancellationToken);
}
