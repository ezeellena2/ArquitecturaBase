using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormDefinitionReader
{
    Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken cancellationToken);
    Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<PagedResult<FormDefinitionListResponse>> ListAsync(ListFormDefinitionsRequest request, bool canEdit, CancellationToken cancellationToken);
    Task<FormDefinitionCounts> CountFacetsAsync(string? search, string displayCulture, CancellationToken cancellationToken);
}
