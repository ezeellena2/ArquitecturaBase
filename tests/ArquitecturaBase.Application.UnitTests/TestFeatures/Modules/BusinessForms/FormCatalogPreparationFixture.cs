using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogPreparationFixture : IFormRecordRepository, IFormCatalogRepository, IFormCatalogFilterReader
{
    public static readonly Dictionary<string, string> Texts = new() { ["es"] = "Personas", ["en"] = "People" };
    public FormCatalogPreparationFixture(bool filtered = false, bool required = true)
    {
        var filters = new FormCatalogFilterDefinition("name", "active", [], true, "name", false);
        Catalog = filtered ? BusinessFormCatalog.CreateFilters(Texts, Texts, JsonSerializer.Serialize(filters, JsonSerializerOptions.Web))
            : BusinessFormCatalog.CreateManual(Texts, Texts);
        FieldId = Guid.NewGuid();
        var field = new CanonicalFormField(FieldId, "field_" + FieldId.ToString("N"), "choice", Texts, null, required, null, null, new(Catalog.Id));
        Version = BusinessFormVersion.Create(DefinitionId, 1, new string('a', 64));
        Version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web));
        Policy = new(this, this, this);
    }
    public Guid DefinitionId { get; } = Guid.NewGuid();
    public Guid FieldId { get; }
    public BusinessFormVersion Version { get; }
    public BusinessFormCatalog Catalog { get; }
    public FormCatalogPreparationPolicy Policy { get; }
    public List<BusinessFormCatalogItem> Items { get; } = [];
    public FormCatalogFilterPreviewResponse Preview { get; set; } = new([], 0, false);
    public bool CatalogMissing { get; set; }
    public Guid? PreviewRecipient { get; private set; }
    public List<Guid> Locks { get; } = [];
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Version.Id ? Version : null);
    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(CatalogMissing ? null : Catalog);
    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken ct) => Task.FromResult(Items);
    public Task LockAsync(Guid id, CancellationToken ct) { Locks.Add(id); return Task.CompletedTask; }
    public Task<FormCatalogFilterPreviewResponse> FindPreviewAsync(Guid id, FormCatalogFilterDefinition filters, CancellationToken ct)
    { PreviewRecipient = id; return Task.FromResult(Preview); }
    public Task LockSourceReceiptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task LockDefinitionsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsPublishedAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task LockCatalogAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsActiveCatalogItemAsync(Guid id, Guid itemId, CancellationToken ct) => throw new NotSupportedException();
    public void Add(BusinessFormRecordFile value) => throw new NotSupportedException();
    public void Add(BusinessFormRecordReference value) => throw new NotSupportedException();
    public void Add(BusinessFormRecord value) => throw new NotSupportedException();
    public void Add(BusinessFormRecordValue value) => throw new NotSupportedException();
    public void Add(BusinessFormCaptureReceipt value) => throw new NotSupportedException();
    public void Add(BusinessFormCatalog value) => throw new NotSupportedException();
    public void AddItems(IEnumerable<BusinessFormCatalogItem> values) => throw new NotSupportedException();
    public Task<IReadOnlyList<FormCatalogFilterChoiceResponse>> ListRolesAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogFilterSampleUserResponse>> ListSampleUsersAsync(FormCatalogFilterOptionsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ExistsActiveSampleUserAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
}
