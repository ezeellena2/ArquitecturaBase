using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormResponseReader
{
    Task<PagedResult<FormNavigationResponse>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken);
    Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<FormResponseStoredRow>> ListAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, CancellationToken cancellationToken);
    Task<PagedResult<FormResponseFacetOptionResponse>> ListFacetsAsync(FormResponseDefinitionRow definition, FormRecordFacetRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormRecordChainStoredRow>> ListChainAsync(Guid recordId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListAncestorDefinitionIdsAsync(Guid recordId, CancellationToken cancellationToken);
    Task<FormResponseStoredRow?> FindRecordAsync(Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListExportIdsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormResponseStoredRow>> ListByReceiptIdsAsync(FormResponseDefinitionRow definition, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);
    Task<long?> FindLatestNumberAsync(Guid id, CancellationToken cancellationToken);
}
