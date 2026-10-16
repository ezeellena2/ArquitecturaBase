using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;
internal sealed class FakeFormResponseReader : IFormResponseReader
{
    public int Reads { get; private set; }
    public Action? BeforeReturn { get; set; }
    public FormResponseDefinitionRow? Definition { get; set; }
    public int DefinitionReads { get; private set; }
    public int ExportSnapshotReads { get; private set; }
    public List<int> ExportBatchSizes { get; } = [];
    public IReadOnlyList<FormResponseStoredRow> ExportRows { get; set; } = [];
    public void ArrangeExport(int count)
    {
        var version = new FormResponseVersionRow(Guid.NewGuid(), 1, "{\"es\":\"Formulario\",\"en\":\"Form\"}", "{\"format\":1,\"questions\":[]}");
        Definition = new(Guid.NewGuid(), "T", version.Id, [version]);
        ExportRows = Enumerable.Range(1, count).Select(number => new FormResponseStoredRow(
            new(Guid.NewGuid(), Guid.NewGuid(), number, Definition.Id, "T", version.Id, 1, version.NameJson, version.SchemaJson, "[]"),
            new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc), new(Guid.NewGuid(), "Employee"))).ToArray();
    }
    public Task<IReadOnlyList<Guid>> ListExportIdsAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, int limit, CancellationToken cancellationToken)
    {
        ExportSnapshotReads++;
        return Task.FromResult<IReadOnlyList<Guid>>(ExportRows.Take(limit).Select(row => row.Record.SourceReceiptId).ToArray());
    }
    public Task<IReadOnlyList<FormResponseStoredRow>> ListByReceiptIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        ExportBatchSizes.Add(ids.Count);
        return Task.FromResult<IReadOnlyList<FormResponseStoredRow>>(ExportRows.Where(row => ids.Contains(row.Record.SourceReceiptId)).ToArray());
    }
    public Task<PagedResult<FormNavigationResponse>> ListNavigationAsync(FormNavigationRequest request, CancellationToken cancellationToken)
    {
        Reads++; BeforeReturn?.Invoke(); return Task.FromResult(new PagedResult<FormNavigationResponse>([new(Guid.NewGuid(), "Private", "P", 1)], request.Page, request.PageSize, 1));
    }
    public Task<FormResponseDefinitionRow?> FindDefinitionAsync(Guid id, CancellationToken cancellationToken) { Reads++; DefinitionReads++; return Task.FromResult(Definition); }
    public Task<PagedResult<FormResponseStoredRow>> ListAsync(FormResponseDefinitionRow definition, ListFormRecordsRequest request, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<FormResponseStoredRow>(ExportRows, request.Page, request.PageSize, ExportRows.Count));
    public Task<PagedResult<FormResponseFacetOptionResponse>> ListFacetsAsync(FormResponseDefinitionRow definition, FormRecordFacetRequest request, CancellationToken cancellationToken) => Task.FromResult(new PagedResult<FormResponseFacetOptionResponse>([], request.Page, request.PageSize, 0));
    public Task<FormResponseStoredRow?> FindRecordAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<FormResponseStoredRow?>(null);
    public Task<long?> FindLatestNumberAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<long?>(1);
}
