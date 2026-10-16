using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormResponseReader : IFormResponseReader
{
    public int Reads { get; private set; }
    public Action? BeforeReturn { get; set; }
    public FormResponseDefinitionRow? Definition { get; set; }
    public Task<PagedResult<FormNavigationResponse>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken)
    {
        Reads++; BeforeReturn?.Invoke(); return Task.FromResult(new PagedResult<FormNavigationResponse>([new(Guid.NewGuid(), "Private", "P", 1)], request.Page, request.PageSize, 1));
    }
    public Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken) { Reads++; return Task.FromResult(Definition); }
    public Task<PagedResult<FormResponseStoredRow>> ListAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<FormResponseStoredRow>([], request.Page, request.PageSize, 0));
    public Task<PagedResult<FormResponseFacetOptionResponse>> ListFacetsAsync(FormResponseDefinitionRow definition, FormRecordFacetRequest request, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<FormResponseFacetOptionResponse>([], request.Page, request.PageSize, 0));
}
