using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormDefinitionReader
{
    Task<FormDefinitionStoredRow?> FindAsync(Guid id, CancellationToken cancellationToken);
    Task<FormPublicationOverviewResponse> FindPublicationAsync(Guid id, Guid? recoveryActor, CancellationToken cancellationToken);
    Task<FormChangeStoredRow?> FindChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<PagedResult<FormDefinitionListStoredRow>> ListAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken);
    /// <summary>The facets of the listing with its search and its folder (CP1-A), without its publication filter.</summary>
    Task<FormDefinitionCounts> CountFacetsAsync(ListFormDefinitionsRequest request, CancellationToken cancellationToken);
}
