using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
public interface IFormPreviewService
{
    Task<Result<StructuredFormPreviewResponse>> GetAsync(Guid definitionId, Guid versionId, string? culture, CancellationToken cancellationToken);
}
