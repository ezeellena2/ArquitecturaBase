using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogImportFixture : IFormCatalogRepository, IFormCatalogReader, IFormSpreadsheetReader, IDisposable
{
    public static readonly string Hash = new('a', 64);
    public Guid Actor { get; } = Guid.NewGuid();
    public BusinessFormCatalog Catalog { get; }
    public List<BusinessFormCatalogItem> Items { get; } = [];
    public FakeUnitOfWork Work { get; } = new();
    public FakePermissionService Permissions { get; } = new();
    public FakeCurrentUser Current { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 22, 4, 0, 0, TimeSpan.Zero));
    public FormCatalogImportService Service { get; }
    public MemoryStream Content { get; } = new([1, 2, 3]);
    public FormSpreadsheet Sheet { get; set; } = new("own.csv", Hash,
        [Row(1, "Code", "Text", "English text", "Detail", "English detail", "Active"), Row(2, "NEW", "Álamo", "Poplar", "Detalle", "Detail", "Yes")]);
    public bool Exists { get; set; } = true;
    public int MetadataReads { get; private set; }
    public int FileReads { get; private set; }
    public int ItemReads { get; private set; }
    public int UsageReads { get; private set; }
    public int UsageCount { get; set; } = 4;
    public List<Guid> LockedIds { get; } = [];
    public int Locks => LockedIds.Count;
    public BusinessFormCatalog? Source { get; private set; }
    public List<BusinessFormCatalogItem> SourceItems { get; } = [];
    public Action? WhileReadingFile { get; set; }
    public Action? WhileLocking { get; set; }
    public Action<int>? BeforeMetadata { get; set; }
    public Error? FileError { get; set; }
    public FormCatalogImportFixture(bool filters = false, BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text)
    {
        Catalog = filters ? BusinessFormCatalog.CreateFilters(Texts(), Texts(), "{}")
            : BusinessFormCatalog.CreateManual(Texts(), Texts(), valueType);
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        Service = new(this, this, this, Work, RequestValidators.For(new FormCatalogImportRequestValidator()),
            new(Current, Permissions), new(this), Clock);
    }
    public FormCatalogImportRequest Request(string mode = "Append") => new(Catalog.Revision, mode, Content, "own.csv", Hash);
    public BusinessFormCatalogItem Add(string code = "OLD", string text = "Viejo", int position = 10, string? value = null)
    {
        var item = Catalog.AddManualItem(code, text, null, null, null, position, value).Value;
        Items.Add(item);
        return item;
    }
    public Guid AddSourceField()
    {
        Source = BusinessFormCatalog.CreateManual(Texts(), Texts(), BusinessFormCatalogValueType.Text);
        SourceItems.Add(Source.AddManualItem("ZONE", "Zona", null, null, null, 0).Value);
        var field = new BusinessFormCatalogField(BusinessFormCatalogField.CreateIdentity(), "zone",
            new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" }, "catalog", Source.Id, false, false, null, null, null, "data", 0, true);
        Assert.True(Catalog.SetFields([field], null).IsSuccess);
        return Source.Id;
    }
    public void Revoke() => Permissions.Permissions[Actor] = [BusinessFormsPermissions.CatalogsRead];
    public void ChangeRevision() => Catalog.UpdateManual(Catalog.Revision, Texts(), Texts());
    public Task<Result<FormSpreadsheet>> ReadAsync(Stream content, string fileName, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.False(Work.InTransaction);
        Assert.Same(Content, content);
        FileReads++;
        WhileReadingFile?.Invoke();
        Result<FormSpreadsheet> result = FileError is { } error ? error : Sheet;
        return Task.FromResult(result);
    }
    public Task LockAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Assert.True(Work.InTransaction);
        LockedIds.Add(id);
        WhileLocking?.Invoke();
        return Task.CompletedTask;
    }
    public Task<BusinessFormCatalog?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(Exists && Catalog.Id == id ? Catalog : null);
    public Task<BusinessFormCatalog?> GetByProposalAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    public Task<List<BusinessFormCatalogItem>> ListItemsAsync(Guid id, CancellationToken ct)
    {
        Assert.True(Work.InTransaction);
        ItemReads++;
        return Task.FromResult(Items.Where(item => item.CatalogId == id).ToList());
    }
    public void Add(BusinessFormCatalog catalog) => throw new NotSupportedException();
    public void AddItems(IEnumerable<BusinessFormCatalogItem> items) { Assert.True(Work.InTransaction); Items.AddRange(items); }
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        if (Source is { } source && id == source.Id) return Task.FromResult<FormCatalogStoredRow?>(new(source.Id, source.Key,
            source.NameJson, source.DescriptionJson, source.Kind.ToString(), source.Revision, source.IsActive, source.CreatedAtUtc,
            source.CreatedBy, source.ModifiedAtUtc, source.ModifiedBy, SourceItems.Count, null, ValueType: source.ValueType.ToString()));
        BeforeMetadata?.Invoke(++MetadataReads);
        return Task.FromResult<FormCatalogStoredRow?>(!Exists || id != Catalog.Id ? null : new(Catalog.Id, Catalog.Key,
            Catalog.NameJson, Catalog.DescriptionJson, Catalog.Kind.ToString(), Catalog.Revision, Catalog.IsActive,
            Catalog.CreatedAtUtc, Catalog.CreatedBy, Catalog.ModifiedAtUtc, Catalog.ModifiedBy, Items.Count,
            Catalog.FiltersJson, ValueType: Catalog.ValueType.ToString(), LastImportSha256: Catalog.LastImportSha256,
            LastImportAtUtc: Catalog.LastImportAtUtc, FieldsJson: Catalog.FieldsJson, PresentationJson: Catalog.PresentationJson));
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct)
    {
        if (Source is { } source && id == source.Id) return Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(Rows(SourceItems));
        Assert.False(Work.InTransaction);
        ItemReads++;
        return Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(Rows(Items));
    }
    private static FormCatalogStoredItemRow[] Rows(IEnumerable<BusinessFormCatalogItem> items) =>
        items.OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new FormCatalogStoredItemRow(item.Id, item.CatalogId, item.Code, item.TextEs, item.TextEn,
                item.DetailEs, item.DetailEn, item.Position, item.IsActive, item.Value, item.FieldValuesJson)).ToArray();
    public Task<int> CountActiveItemsAsync(Guid catalogId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<int> CountUsageAsync(Guid id, CancellationToken ct) { UsageReads++; return Task.FromResult(UsageCount); }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public static FormSpreadsheetRow Row(int number, params string[] cells) => new(number, cells.Select(value => new FormSpreadsheetCell(value)).ToArray());
    private static Dictionary<string, string> Texts() => new() { ["es"] = "Propio", ["en"] = "Own" };
    public void Dispose() => Content.Dispose();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> FindFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) => Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>([]);
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken) => throw new NotSupportedException();
}
