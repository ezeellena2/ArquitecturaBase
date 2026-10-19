using System.Globalization;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

internal sealed class BusinessFormCaptureFixture
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    public FakeFormResponseReader OriginResponses { get; } = new();
    public FormSuggestionFixture Suggestions { get; } = new();
    public FormActionFixture Actions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; }
    public BusinessFormVersion Version { get; }
    public BusinessFormCaptureFixture(string type, bool required, Guid? catalogId = null, Action? onCommit = null)
    {
        var fieldId = Guid.NewGuid();
        Key = $"field_{fieldId:N}";
        Definition = BusinessFormDefinition.Create();
        var definition = new Dictionary<string, string> { ["es"] = "Formulario" };
        var schemaDraft = "{}";
        Assert.True(Definition.SetDraft(1, "Formulario", JsonSerializer.Serialize(definition), "FORM", "es",
            "{\"es\":\"Enviar\"}", schemaDraft, 1, creating: true).IsSuccess);
        var version = BusinessFormVersion.Create(Definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1,
            [new CanonicalFormField(fieldId, Key, type, definition, null, required, type is "choice" or "photo" or "document" ? null : 100, null, catalogId.HasValue ? new FormQuestionSource(catalogId) : null, type is "photo" or "document" ? new(required ? 1 : 0, 3, type == "photo" ? "camera" : null) : null)]), SerializerOptions));
        version.SealPresentation("{\"es\":\"Formulario\"}", "{\"es\":\"Enviar\"}", "es", 1);
        Assert.True(Definition.ActivateVersion(null, version.Id).IsSuccess);
        Version = version;
        Repository = new FakeRecordRepository(Definition, version);
        Permission = new MutablePermissionService();
        Permission.Allowed = true;
        ReceiptReader = new FakeCaptureReceiptReader(Repository);
        StoredFiles = new TestFeatures.Files.MemoryStoredFileRepository();
        var preparer = new BusinessFormFileRecorder(new TestFeatures.Files.MemoryPrivateFileStore(), new TestFeatures.Files.FakeFileContentProcessor(), Microsoft.Extensions.Options.Options.Create(new ArquitecturaBase.Application.Modules.BusinessForms.Configuration.BusinessFormsOptions()), Repository, StoredFiles);
        UnitOfWork = new FakeUnitOfWork { OnCommit = onCommit };
        Sink = new BusinessFormRecordRecorder(Repository, new(ReceiptReader, Permission), UnitOfWork, Actions.Clock,
            NullLogger<BusinessFormRecordRecorder>.Instance, preparer, Actions.Recorder, new(OriginResponses, Suggestions));
        VersionId = version.Id;
    }

    public const string SourcePrefix = "unused";
    public Guid Actor { get; } = Guid.NewGuid();
    public string Key { get; }
    public Guid VersionId { get; }
    public BusinessFormDefinition Definition { get; }
    public FakeRecordRepository Repository { get; }
    public FakeCaptureReceiptReader ReceiptReader { get; }
    public MutablePermissionService Permission { get; }
    public BusinessFormRecordRecorder Sink { get; }
    public TestFeatures.Files.MemoryStoredFileRepository StoredFiles { get; }
    public StructuredRecordCaptureRequest Request(IReadOnlyDictionary<string, string> answers) =>
        new(Guid.Parse("00000000-0000-7000-8000-000000000123"), Definition.Id, VersionId, Actor,
            DateTime.SpecifyKind(DateTime.Parse("2026-10-11T12:00:00", CultureInfo.InvariantCulture), DateTimeKind.Utc), answers);
}

internal sealed class FakeCaptureReceiptReader(FakeRecordRepository repository) : IFormCaptureReceiptReader
{
    public bool SkipRead { get; set; }
    public Task<FormCaptureReceiptRow?> FindAsync(Guid sourceReceiptId, CancellationToken cancellationToken)
    {
        if (SkipRead) return Task.FromResult<FormCaptureReceiptRow?>(null);
        var receipt = repository.Receipts.SingleOrDefault(value => value.SourceReceiptId == sourceReceiptId);
        return Task.FromResult(receipt is null ? null : new FormCaptureReceiptRow(receipt.SourceReceiptId,
            receipt.DefinitionId, receipt.VersionId, receipt.Fingerprint, receipt.RecordId, receipt.Number,
            receipt.State, receipt.ErrorCode, receipt.RejectedFieldId));
    }
}

internal sealed class FakeRecordRepository(BusinessFormDefinition definition, BusinessFormVersion version) : IFormRecordRepository
{
    public static FakeRecordRepository Empty()
    {
        var definition = BusinessFormDefinition.Create();
        return new(definition, BusinessFormVersion.Create(definition.Id, 1, "test"));
    }
    public bool ItemActive { get; set; } = true;
    public List<BusinessFormRecordReference> References { get; } = [];
    public Task LockCatalogAsync(Guid catalogId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<bool> ExistsActiveCatalogItemAsync(Guid catalogId, Guid itemId, CancellationToken cancellationToken) => Task.FromResult(ItemActive);
    public void Add(BusinessFormRecordReference reference) => References.Add(reference);
    public List<BusinessFormRecord> Records { get; } = [];
    public List<BusinessFormRecordValue> Values { get; } = [];
    private readonly List<BusinessFormCaptureReceipt> _receipts = [];
    public IReadOnlyList<BusinessFormCaptureReceipt> Receipts => _receipts;
    public Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        Task.FromResult(_receipts.SingleOrDefault(value => value.SourceReceiptId == sourceReceiptId));
    public Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormDefinition?>(id == definition.Id ? definition : null);
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormVersion?>(id == version.Id ? version : null);
    public Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken) => Task.FromResult(versionId == version.Id);
    public List<BusinessFormRecordFile> FileReferences { get; } = [];
    public void Add(BusinessFormRecordFile file) => FileReferences.Add(file);
    public void Add(BusinessFormRecord record) => Records.Add(record);
    public void Add(BusinessFormRecordValue value) => Values.Add(value);
    public void Add(BusinessFormCaptureReceipt receipt) => _receipts.Add(receipt);
}

internal sealed class MutablePermissionService : IPermissionService
{
    private int _checks;
    public bool Allowed { get; set; }
    public bool RevokeOnSecondCheck { get; set; }
    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(Allowed ? [BusinessFormsPermissions.RecordsCreate] : []);
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _checks);
        return Task.FromResult(Allowed && !(RevokeOnSecondCheck && count >= 2));
    }

}
