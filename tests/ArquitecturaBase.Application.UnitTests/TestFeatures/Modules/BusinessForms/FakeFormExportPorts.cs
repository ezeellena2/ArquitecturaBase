using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Results;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormWorkbookWriter : IFormWorkbookWriter
{
    public FormWorkbookRequest? Request { get; private set; }
    public Action? BeforeReturn { get; set; }
    public byte[] Write(FormWorkbookRequest request, CancellationToken cancellationToken) { Request = request; BeforeReturn?.Invoke(); return [1]; }
}
internal sealed class FakeFormExportResponses : IFormResponseService
{
    public int Calls { get; private set; }
    public ListFormRecordsRequest? LastRequest { get; private set; }
    public FormResponseSheetResponse Sheet { get; set; } = new(Guid.NewGuid(), "Form", "T", 1, [], [], new([], 1, 100, 10001));
    public Task<Result<FormResponseSheetResponse>> ListAsync(Guid id, ListFormRecordsRequest request, CancellationToken cancellationToken) { Calls++; LastRequest = request; return Task.FromResult(Result.Success(Sheet)); }
    public Task<Result<PagedResult<FormNavigationResponse>>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<PagedResult<FormResponseFacetOptionResponse>>> CountFacetsAsync(Guid id, FormRecordFacetRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Result<FormResponseDetailResponse>> GetAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();
}
