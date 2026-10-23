using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Domain.Modules.BusinessForms;
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
    public FormCatalogPersonReader People { get; } = new();
    public IReadOnlyList<BusinessFormCatalogField> Fields { get; set; } = [];
    public FormCatalogPresentation? Presentation { get; set; }
    public IReadOnlyList<FormCatalogStoredItemRow> Items { get; set; } = [];
    public Dictionary<Guid, IReadOnlyList<FormCatalogStoredItemRow>> Sources { get; } = [];
    public FormCatalogMaximumOfferRow Maximum { get; set; } = new(0, "{}");
    public List<(string? Fixed, Guid[] Fields)> MaximumReads { get; } = [];

    public FormPublicationGuard Guard => new(null!, this, null!, Options.Create(new BusinessFormsOptions()), People);
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
            1, IsActive, DateTime.UnixEpoch, null, null, null, ActiveItems,
            FieldsJson: Fields.Count == 0 ? null : FormDraftPolicy.Serialize(Fields), PresentationJson: Presentation is null ? null : FormDraftPolicy.Serialize(Presentation)));
    }
    public Task<int> CountActiveItemsAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); CountReads.Add(id); return Task.FromResult(ActiveItems);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(id == CatalogId ? Items : Sources.GetValueOrDefault(id) ?? []);
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson, IReadOnlyCollection<Guid> personFieldIds, CancellationToken ct)
    {
        MaximumReads.Add((fixedContainmentJson, personFieldIds.Order().ToArray()));
        return Task.FromResult(Maximum);
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
}
