using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Configuration;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormPublicationMultipleCatalogFixture : IFormCatalogReader
{
    public Guid CatalogId { get; } = Guid.CreateVersion7();
    public string Kind { get; set; } = "Manual";
    public bool IsActive { get; set; } = true;
    public bool Missing { get; set; }
    public int ActiveItems { get; set; }
    public List<Guid> CatalogReads { get; } = [];
    public List<Guid> CountReads { get; } = [];

    public FormPublicationGuard Guard => new(null!, this, null!, Options.Create(new BusinessFormsOptions()));
    public CanonicalFormField Field(string type = "multiChoice")
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = "Opciones" },
            null, true, null, null, new(CatalogId), Selection: type == "multiChoice" ? new(1, 3) : null);
    }

    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); CatalogReads.Add(id);
        return Task.FromResult<FormCatalogStoredRow?>(Missing ? null : new(CatalogId, "CATALOG", "{}", "{}", Kind,
            1, IsActive, DateTime.UnixEpoch, null, null, null, ActiveItems));
    }
    public Task<int> CountActiveItemsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); CountReads.Add(id); return Task.FromResult(ActiveItems);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
}
