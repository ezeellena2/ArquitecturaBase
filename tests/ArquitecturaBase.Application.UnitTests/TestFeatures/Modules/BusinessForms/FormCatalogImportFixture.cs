using System.Security.Cryptography;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.BusinessForms.Channels;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.Modules.BusinessForms.Validation;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class FormCatalogImportFixture : IFormCatalogRepository, IFormCatalogReader, IFormSpreadsheetReader, IDisposable
{
    public static readonly string Hash = new('a', 64);
    public Guid Actor { get; } = Guid.NewGuid();
    public BusinessFormCatalog Catalog { get; private set; }
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
    public List<FormCatalogFieldUsageRow> FieldUses { get; } = [];
    public int Locks => LockedIds.Count;
    private readonly List<BusinessFormCatalog> sources = [];
    private readonly List<BusinessFormCatalogItem> sourceItems = [];
    public Action? WhileReadingFile { get; set; }
    public Action? WhileLocking { get; set; }
    public Action<int>? BeforeMetadata { get; set; }
    public Error? FileError { get; set; }

    // Stage 2: the proposal of this person's Home turn and the private copy of its spreadsheet, which is read as its bytes say.
    public StubAssistantProposalLookup Lookup { get; } = new();
    public MemoryPrivateFileStore Files { get; }
    public Guid ProposalId { get; } = Guid.NewGuid();
    public byte[] Copy { get; set; } = [80, 75, 3, 4, 20];
    public FormSpreadsheet CopySheet { get; set; } = new("Hoja1", "", [Row(1, "CUIT", "Razón social", "Teléfono"),
        Row(2, "20-1", "Transportes Sur", "11 5555-0001"), Row(3, "20-2", "Logística Pampa", "")]);
    public int CopyReads { get; private set; }

    // propose-catalog@2 with the columns of the turn's spreadsheet: the code from A, the option from B and «Teléfono» from C.
    public FormAuthoringMappedCatalogProposal Mapped { get; } = new("es", "Transportistas", "Empresas que traen la carga", "text", "CUIT",
        "Razón social", [new("Teléfono", "text")], new("transportistas.xlsx", "Hoja1", 2, true,
            [new("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social"), new("field", "Teléfono", null, "C", "Teléfono")],
            [["20-1", "Transportes Sur", "11 5555-0001"], ["20-2", "Logística Pampa", ""]]));

    public FormCatalogImportFixture(bool filters = false, BusinessFormCatalogValueType valueType = BusinessFormCatalogValueType.Text,
        string originalCulture = "es", bool storage = true)
    {
        Catalog = filters ? BusinessFormCatalog.CreateFilters(originalCulture, Texts(), Texts(), "{}")
            : BusinessFormCatalog.CreateManual(originalCulture, Texts(), Texts(), valueType);
        Current.UserId = Actor;
        Permissions.Permissions[Actor] = [BusinessFormsPermissions.CatalogsRead, BusinessFormsPermissions.CatalogsManage];
        Files = new(storage);
        var access = new FormCatalogAccessGuard(Current, Permissions);
        Service = new(this, this, Work, RequestValidators.For(new FormCatalogImportRequestValidator(), new FormCatalogAssistantImportRequestValidator()),
            access, new FormCatalogImportRecorder(this, this, access, new(this), Clock), new FormCatalogAssistantImportPolicy(Lookup, Files, this));
    }
    public FormCatalogImportRequest Request(string mode = "Append") => new(Catalog.Revision, mode, Content, "own.csv", Hash);
    public FormCatalogAssistantImportRequest AssistantRequest(string? sheetSha256 = null) => new(Lookup.RequestId, ProposalId, Catalog.Revision, sheetSha256);

    // «Crear catálogo» on that proposal: inactive, pending, with the proposal's fields and, from Home, the turn's request in its evidence.
    public void ProposeCatalog(bool fromHome = true)
    {
        var provenance = Provenance(fromHome ? "home" : "whatsapp-assistant");
        var evidence = JsonSerializer.Serialize(new FormCatalogProposalEvidence("es", Actor, provenance.OccurredAtUtc, Guid.NewGuid(),
            BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, BusinessFormAuthoringAssistantCapabilitySource.MappedCatalogVersion,
            provenance, null, 0, Mapped.ToCatalog(), fromHome ? "app" : null, fromHome ? Lookup.RequestId : null), JsonSerializerOptions.Web);
        Catalog = BusinessFormCatalog.CreateProposedManual("es", Texts(), Texts(), BusinessFormCatalogValueType.Text, ProposalId, evidence);
        Assert.True(Catalog.SetFields(FormAuthoringProposalPolicy.CatalogFields(Mapped.ToCatalog()), null).IsSuccess);
    }

    // propose-import@1 into a catalog: its code from A and its option from B.
    public static FormAuthoringImportProposal Import(Guid catalogId) => new(new(catalogId, "Propio", "text"), new("transportistas.xlsx", "Hoja1", 2,
        true, [new("code", null, null, "A", "CUIT"), new("option", null, null, "B", "Razón social")],
        [["20-1", "Transportes Sur"], ["20-2", "Logística Pampa"]]));

    // The lookup answers this person with that proposal and the descriptor of its turn's copy, which sits in private storage.
    public async Task<AssistantAttachmentDescriptor> HandAsync(AssistantCapabilityPayload payload,
        string capability = BusinessFormAuthoringAssistantCapabilitySource.ProposeCatalogId, int version = 2, bool current = true, bool store = true)
    {
        var copy = new AssistantAttachmentDescriptor($"ai/assistant/attachments/{Lookup.RequestId:N}.xlsx", "transportistas.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", Copy.LongLength, Sha256(Copy));
        Lookup.OwnerUserId = Actor;
        Lookup.Response = new AssistantProposalLookupResponse(new("home", Actor, null, "es", "America/Argentina/Buenos_Aires", new([], []), [], []),
            new(Guid.NewGuid(), ProposalId, new(capability, version), payload), Provenance("home"), current) { Attachment = copy };
        using var content = new MemoryStream(Copy, false);
        if (store) await Files.WriteAsync(copy.StorageKey, content, copy.ContentType, CancellationToken.None);
        return copy;
    }

    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));
    private AssistantOperationProvenance Provenance(string channelId) =>
        new("fictitious-profile", 3, "home-system", 2, Clock.GetUtcNow().UtcDateTime.AddMinutes(-2), channelId);
    public BusinessFormCatalogItem Add(string code = "OLD", string text = "Viejo", int position = 10, string? value = null)
    {
        var item = Catalog.AddManualItem(code, text, null, null, null, position, value).Value;
        Items.Add(item);
        return item;
    }
    /// <summary>Adds two catalog fields whose sources are stored in descending id order, so only a sort yields ascending locks.</summary>
    public Guid[] AddSourceFields()
    {
        Guid[] ids = [AddSource(), AddSource()];
        var fields = ids.OrderDescending().Select((sourceId, position) => new BusinessFormCatalogField(
            BusinessFormCatalogField.CreateIdentity(), $"zone_{position}", new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" },
            "catalog", sourceId, false, false, null, null, null, "data", position, true)).ToArray();
        Assert.True(Catalog.SetFields(fields, null).IsSuccess);
        return ids;
    }
    public BusinessFormCatalogField AddTextField()
    {
        var field = new BusinessFormCatalogField(BusinessFormCatalogField.CreateIdentity(), "zone",
            new Dictionary<string, string> { ["es"] = "Zona", ["en"] = "Zone" }, "text", null, false, false, null, null, 300, "data", 0, true);
        Assert.True(Catalog.SetFields([field], null).IsSuccess);
        return field;
    }
    private Guid AddSource()
    {
        var source = BusinessFormCatalog.CreateManual("es", Texts(), Texts(), BusinessFormCatalogValueType.Text);
        sources.Add(source);
        sourceItems.Add(source.AddManualItem("ZONE", "Zona", null, null, null, 0).Value);
        return source.Id;
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
    // An upload is read by the extension of its file; the assistant's copy, as its bytes say, with the SHA-256 of those bytes.
    public FormSpreadsheetFormat DetectFormat(byte[] content)
    {
        Assert.False(Work.InTransaction);
        Assert.Equal(Copy, content);
        return FormSpreadsheetFormat.Xlsx;
    }
    public Result<FormSpreadsheet> Read(byte[] content, FormSpreadsheetFormat format, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.False(Work.InTransaction);
        Assert.Equal(Copy, content);
        Assert.Equal(FormSpreadsheetFormat.Xlsx, format);
        CopyReads++;
        WhileReadingFile?.Invoke();
        return CopySheet with { Sha256 = Sha256(content) };
    }
    // Importing reads the first sheet only; the rows of every sheet are read to load them (VC1-A).
    public Result<IReadOnlyList<FormSpreadsheet>> ReadSheets(byte[] content, FormSpreadsheetFormat format, CancellationToken cancellationToken) =>
        throw new NotSupportedException("An import never reads every sheet.");
    public Task LockKeyAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<Guid?> FindIdByKeyAsync(string key, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);
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
    public void RemoveItems(IEnumerable<BusinessFormCatalogItem> items) => throw new NotSupportedException();
    public Task<FormCatalogStoredRow?> FindAsync(Guid id, CancellationToken ct)
    {
        if (sources.Find(source => source.Id == id) is { } source) return Task.FromResult<FormCatalogStoredRow?>(new(source.Id, source.Key,
            source.NameJson, source.DescriptionJson, source.OriginalCulture, source.Kind.ToString(), source.Revision, source.IsActive, source.CreatedAtUtc,
            source.CreatedBy, source.ModifiedAtUtc, source.ModifiedBy, sourceItems.Count(item => item.CatalogId == id), null,
            ValueType: source.ValueType.ToString()));
        BeforeMetadata?.Invoke(++MetadataReads);
        return Task.FromResult<FormCatalogStoredRow?>(!Exists || id != Catalog.Id ? null : new(Catalog.Id, Catalog.Key,
            Catalog.NameJson, Catalog.DescriptionJson, Catalog.OriginalCulture, Catalog.Kind.ToString(), Catalog.Revision, Catalog.IsActive,
            Catalog.CreatedAtUtc, Catalog.CreatedBy, Catalog.ModifiedAtUtc, Catalog.ModifiedBy, Items.Count,
            Catalog.FiltersJson, Catalog.ProposalEvidenceJson, Catalog.ProposalState, ValueType: Catalog.ValueType.ToString(),
            LastImportSha256: Catalog.LastImportSha256, LastImportAtUtc: Catalog.LastImportAtUtc, FieldsJson: Catalog.FieldsJson,
            PresentationJson: Catalog.PresentationJson, ProposalId: Catalog.ProposalId));
    }
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListAllItemsAsync(Guid id, CancellationToken ct)
    {
        if (sources.Exists(source => source.Id == id))
            return Task.FromResult<IReadOnlyList<FormCatalogStoredItemRow>>(Rows(sourceItems.Where(item => item.CatalogId == id)));
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
    public Task<IReadOnlyList<FormCatalogStoredItemRow>> ListOfferItemsAsync(Guid id, CancellationToken ct, string? fieldValueContainmentJson = null, int limit = 201) => throw new NotSupportedException();
    public Task<FormCatalogsPageResponse> FindPageAsync(ListFormCatalogsRequest request, bool canEdit, bool canReadForms, CancellationToken ct) => throw new NotSupportedException();
    public Task<PagedResult<FormCatalogItemResponse>> ListItemsAsync(Guid id, ListFormCatalogItemsRequest request, CancellationToken ct) => throw new NotSupportedException();
    public static FormSpreadsheetRow Row(int number, params string[] cells) => new(number, cells.Select(value => new FormSpreadsheetCell(value)).ToArray());
    private static Dictionary<string, string> Texts() => new() { ["es"] = "Propio", ["en"] = "Own" };
    public void Dispose() => Content.Dispose();
    public Task<IReadOnlyList<FormCatalogFieldUsageRow>> ListFieldUsageAsync(Guid catalogId, IReadOnlyCollection<Guid> fieldIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FormCatalogFieldUsageRow>>(FieldUses.Where(use => fieldIds.Contains(use.FieldId)).ToArray());
    public Task<bool> ExistsSourceUsageAsync(Guid catalogId, CancellationToken ct) => Task.FromResult(false);
    public Task<IReadOnlyList<FormCatalogItemUsageRow>> ListItemUsesAsync(Guid catalogId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) => throw new NotSupportedException();
    public Task<FormCatalogMaximumOfferRow> FindMaximumOfferSizeAsync(Guid catalogId, string? fixedContainmentJson,
        IReadOnlyCollection<Guid> personFieldIds, CancellationToken cancellationToken, Guid? groupByFieldId = null) => throw new NotSupportedException();
}
