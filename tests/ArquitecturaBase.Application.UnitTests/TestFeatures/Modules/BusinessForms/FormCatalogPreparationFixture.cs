using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogPreparationFixture : IFormRecordRepository, IFormCatalogRepository, IFormCatalogReader, IFormCatalogFilterReader, IFormPdfDesignReader
{
    public static readonly Dictionary<string, string> Texts = new() { ["es"] = "Personas", ["en"] = "People" };
    public FormCatalogPreparationFixture(bool filtered = false, bool required = true, string originalCulture = "es", bool originalOnly = false,
        BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text, string questionType = "choice", FormSelectionSettings? selection = null)
    {
        var filters = new FormCatalogFilterDefinition("name", "active", [], true, "name", false);
        Catalog = filtered ? BusinessFormCatalog.CreateFilters(Texts, Texts, JsonSerializer.Serialize(filters, JsonSerializerOptions.Web))
            : BusinessFormCatalog.CreateManual(Texts, Texts, valueType);
        FieldId = Guid.NewGuid();
        var formTexts = originalOnly ? new Dictionary<string, string> { [originalCulture] = Texts[originalCulture] } : Texts;
        var field = new CanonicalFormField(FieldId, "field_" + FieldId.ToString("N"), questionType, formTexts, null, required, null, null, new(Catalog.Id), Selection: selection);
        Version = BusinessFormVersion.Create(DefinitionId, 1, new string('a', 64));
        Version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, [field]), JsonSerializerOptions.Web));
        Version.SealPresentation(JsonSerializer.Serialize(formTexts), JsonSerializer.Serialize(formTexts), originalCulture, 1);
        Policy = new(this, this, new FormCatalogOptionsPolicy(this, this), new(Rules, this));
    }
    public FormRuleFixture Rules { get; } = new();
    public FormPdfDesignStoredRow? CurrentDesign { get; set; }
    Task<FormPdfDesignStoredRow?> IFormPdfDesignReader.FindCurrentAsync(Guid id, CancellationToken ct) => Task.FromResult(CurrentDesign);
    Task<FormRuleDefinitionRow?> IFormPdfDesignReader.FindDefinitionAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    Task<FormPdfDesignStoredRow?> IFormPdfDesignReader.FindRevisionAsync(Guid id, long revision, CancellationToken ct) => throw new NotSupportedException();
    Task<FormPdfDocumentRow?> IFormPdfDesignReader.FindDocumentAsync(Guid id, Guid document, CancellationToken ct) => throw new NotSupportedException();
    public void SetRules(IReadOnlyList<FormRule> rules)
    {
        var set = BusinessFormRuleSet.Create(DefinitionId, 1, Version.Culture!, FormDraftPolicy.Serialize(rules), false, Version.SchemaJson);
        Rules.RuleSets.Add(set); Rules.Definition.SetActiveRuleSetId(set.Id);
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
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid catalogId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(Items.Where(item => item.CatalogId == catalogId).OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn, item.Position, item.IsActive, item.Value)).ToArray());
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid catalogId, CancellationToken ct, string? fieldValueContainmentJson = null) =>
        Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(Items.Where(item => item.IsActive).OrderBy(item => item.Position).ThenBy(item => item.Id).Take(201)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn, item.DetailEs, item.DetailEn, item.Position, item.IsActive, item.Value)).ToArray());
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct) => Task.FromResult<FormCatalogStoredRow?>(CatalogMissing ? null :
        new(Catalog.Id, Catalog.Key, Catalog.NameJson, Catalog.DescriptionJson, Catalog.Kind.ToString(), Catalog.Revision, Catalog.IsActive,
            Catalog.CreatedAtUtc, Catalog.CreatedBy, Catalog.ModifiedAtUtc, Catalog.ModifiedBy, Items.Count, Catalog.FiltersJson, ValueType: Catalog.ValueType.ToString()));
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();

    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken ct) => Task.FromResult(id == Version.Id ? Version : null);
    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid id, CancellationToken ct) => Task.FromResult<BusinessFormCatalog?>(null);
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
