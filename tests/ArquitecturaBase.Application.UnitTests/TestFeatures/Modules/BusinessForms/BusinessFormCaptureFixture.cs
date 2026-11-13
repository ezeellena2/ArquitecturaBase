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
    /// <summary>Task 7: the inbox the capture confirms a received document in.</summary>
    public FormReceivedDocumentFixture Documents { get; } = new();
    public FormSuggestionFixture Suggestions { get; } = new();
    public FormActionFixture Actions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; }
    public BusinessFormVersion Version { get; }
    public BusinessFormCaptureFixture(string type, bool required, Guid? catalogId = null, Action? onCommit = null, bool withPhoto = false,
        FormSelectionSettings? selection = null, IReadOnlyList<CanonicalFormChoice>? choices = null)
        : this(Questions(type, required, catalogId, withPhoto, selection, choices), onCommit) { }

    // A published version with these questions, such as a block of rows; Key is the first question's.
    public BusinessFormCaptureFixture(IReadOnlyList<CanonicalFormField> questions, Action? onCommit = null)
    {
        Key = questions[0].Key;
        PhotoFieldId = questions.SingleOrDefault(question => question.Key == "photo")?.FieldId;
        Definition = BusinessFormDefinition.Create();
        var definition = new Dictionary<string, string> { ["es"] = "Formulario" };
        var schemaDraft = "{}";
        Assert.True(Definition.SetDraft(1, "Formulario", JsonSerializer.Serialize(definition), "FORM", "es",
            "{\"es\":\"Enviar\"}", schemaDraft, 1, creating: true).IsSuccess);
        var version = BusinessFormVersion.Create(Definition.Id, 1, new string('a', 64));
        version.SealSchema(JsonSerializer.Serialize(new FormVersionSchema(1, questions), SerializerOptions));
        version.SealPresentation("{\"es\":\"Formulario\"}", "{\"es\":\"Enviar\"}", "es", questions.Count);
        Assert.True(Definition.ActivateVersion(null, version.Id).IsSuccess);
        Version = version;
        Repository = new FakeRecordRepository(Definition, version);
        Permission = new MutablePermissionService();
        Permission.Allowed = true;
        ReceiptReader = new FakeCaptureReceiptReader(Repository);
        StoredFiles = new TestFeatures.Files.MemoryStoredFileRepository();
        var preparer = new BusinessFormFileRecorder(FileStore, new TestFeatures.Files.FakeFileContentProcessor(), Microsoft.Extensions.Options.Options.Create(new ArquitecturaBase.Application.Modules.BusinessForms.Configuration.BusinessFormsOptions()), Repository, StoredFiles);
        UnitOfWork = new FakeUnitOfWork { OnCommit = onCommit };
        Actor = Users.AddUser("capture@example.invalid").Id;
        Current.UserId = Actor;
        Issuances = new FakeRecordIssuanceReader(() => UnitOfWork.InTransaction);
        Sink = new BusinessFormRecordRecorder(Repository, new(ReceiptReader, Permission, new(Current, Permission, Users), Issuances, Users), UnitOfWork, Actions.Clock,
            NullLogger<BusinessFormRecordRecorder>.Instance, preparer, Actions.Recorder,
            new(OriginResponses, Suggestions, Documents, Documents.Recorder(), Documents.Clock, new(Documents, new(Documents, Documents, [Documents]))));
        VersionId = version.Id;
    }

    private static List<CanonicalFormField> Questions(string type, bool required, Guid? catalogId, bool withPhoto,
        FormSelectionSettings? selection, IReadOnlyList<CanonicalFormChoice>? choices)
    {
        var fieldId = Guid.NewGuid();
        var label = new Dictionary<string, string> { ["es"] = "Formulario" };
        var questions = new List<CanonicalFormField>
        {
            new(fieldId, $"field_{fieldId:N}", type, label, null, required, type is "choice" or "multiChoice" or "photo" or "document" ? null : 100, choices,
                catalogId.HasValue ? new FormQuestionSource(catalogId) : null,
                type is "photo" or "document" ? new(required ? 1 : 0, 3, type == "photo" ? "camera" : null) : null, Selection: selection)
        };
        if (withPhoto) questions.Add(new(Guid.NewGuid(), "photo", "photo", label, null, true, null, null, null, new(1, 3, "camera")));
        return questions;
    }

    public const string SourcePrefix = "unused";
    public Guid Actor { get; }
    public TestDoubles.Users.InMemoryUserAccounts Users { get; } = new();
    public TestDoubles.Auth.FakeCurrentUser Current { get; } = new();
    public string Key { get; }
    public Guid? PhotoFieldId { get; }
    public Guid VersionId { get; }
    public BusinessFormDefinition Definition { get; }
    public FakeRecordRepository Repository { get; }
    public FakeCaptureReceiptReader ReceiptReader { get; }
    public MutablePermissionService Permission { get; }
    public FakeRecordIssuanceReader Issuances { get; }
    public BusinessFormRecordRecorder Sink { get; }
    public TestFeatures.Files.MemoryPrivateFileStore FileStore { get; } = new();
    public TestFeatures.Files.MemoryStoredFileRepository StoredFiles { get; }
    public StructuredRecordCaptureRequest Request(IReadOnlyDictionary<string, string> answers) =>
        new(Guid.Parse("00000000-0000-7000-8000-000000000123"), Definition.Id, VersionId, Actor,
            DateTime.SpecifyKind(DateTime.Parse("2026-10-11T12:00:00", CultureInfo.InvariantCulture), DateTimeKind.Utc), answers);
}

/// <summary>The responses with a live issuance, and each read with whether it ran inside the capture's boundary.</summary>
internal sealed class FakeRecordIssuanceReader(Func<bool> inTransaction) : IFormRecordIssuanceReader
{
    public HashSet<Guid> Live { get; } = [];
    public List<(Guid RecordId, bool InTransaction)> Reads { get; } = [];
    public Task<bool> ExistsLiveAsync(Guid recordId, CancellationToken cancellationToken)
    {
        Reads.Add((recordId, inTransaction()));
        return Task.FromResult(Live.Contains(recordId));
    }
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
            receipt.State, receipt.ErrorCode, receipt.RejectedFieldId, receipt.RejectedRow));
    }
}

internal sealed class FakeRecordRepository(BusinessFormDefinition definition, BusinessFormVersion version) : IFormRecordRepository
{
    public static FakeRecordRepository Empty()
    {
        var definition = BusinessFormDefinition.Create();
        return new(definition, BusinessFormVersion.Create(definition.Id, 1, "test"));
    }
    public Guid DefinitionId => definition.Id;
    public Dictionary<Guid, FormCatalogItemFieldValuesRow> CatalogFieldValues { get; } = [];
    public List<Guid[]> FieldValueBatches { get; } = [];
    public List<Guid[]> FieldValueReadLocks { get; } = [];
    public Task<IReadOnlyList<FormCatalogItemFieldValuesRow>> ListCatalogFieldValuesAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        FieldValueBatches.Add(itemIds.ToArray());
        FieldValueReadLocks.Add(CatalogLocks.ToArray());
        return Task.FromResult<IReadOnlyList<FormCatalogItemFieldValuesRow>>(itemIds.Select(id =>
            CatalogFieldValues.TryGetValue(id, out var row) ? row
                : new FormCatalogItemFieldValuesRow(id, ActivityReads.Last(value => value.ItemId == id).CatalogId, null, "{}")).ToArray());
    }
    public bool ItemActive { get; set; } = true;
    public HashSet<Guid> InactiveItems { get; } = [];
    public List<Guid> CatalogLocks { get; } = [];
    public List<(Guid CatalogId, Guid ItemId)> ActivityReads { get; } = [];
    public List<BusinessFormRecordReference> References { get; } = [];
    public Task LockCatalogAsync(Guid catalogId, CancellationToken cancellationToken)
    {
        CatalogLocks.Add(catalogId);
        return Task.CompletedTask;
    }
    public Task<bool> ExistsActiveCatalogItemAsync(Guid catalogId, Guid itemId, CancellationToken cancellationToken)
    {
        ActivityReads.Add((catalogId, itemId));
        return Task.FromResult(ItemActive && !InactiveItems.Contains(itemId));
    }
    public void Add(BusinessFormRecordReference reference) => References.Add(reference);
    public List<BusinessFormRecord> Records { get; } = [];
    public List<BusinessFormRecordValue> Values { get; } = [];
    private readonly List<BusinessFormCaptureReceipt> _receipts = [];
    public IReadOnlyList<BusinessFormCaptureReceipt> Receipts => _receipts;
    /// <summary>Runs when the capture enters its unit of work, after the checks made before it: what changed meanwhile.</summary>
    public Func<Task>? OnLockSourceReceipt { get; set; }
    public Task LockSourceReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) => OnLockSourceReceipt?.Invoke() ?? Task.CompletedTask;
    public Task LockDefinitionsAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<BusinessFormCaptureReceipt?> GetReceiptAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        Task.FromResult(_receipts.SingleOrDefault(value => value.SourceReceiptId == sourceReceiptId));
    public Task<BusinessFormDefinition?> GetDefinitionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormDefinition?>(id == definition.Id ? definition : null);
    public Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<BusinessFormVersion?>(id == version.Id ? version : null);
    public Task<bool> ExistsPublishedAsync(Guid versionId, CancellationToken cancellationToken) => Task.FromResult(versionId == version.Id);
    public List<BusinessFormRecordFile> FileReferences { get; } = [];
    public List<BusinessFormTask> Tasks { get; } = [];
    /// <summary>Stored metadata of the files a correction may keep, by file.</summary>
    public Dictionary<Guid, FormRecordFileRow> StoredFileRows { get; } = [];
    public Task<BusinessFormRecord?> GetRecordAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Records.SingleOrDefault(record => record.Id == recordId));
    public Task<BusinessFormTask?> GetOpenTaskAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.FromResult(Tasks.SingleOrDefault(task => task.RecordId == recordId && task.State == FormTaskState.Open));
    public Task<IReadOnlyList<FormRecordFileRow>> ListFilesAsync(Guid recordId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormRecordFileRow>>(FileReferences.Where(file => file.RecordId == recordId).OrderBy(file => file.Position)
            .Select(file => StoredFileRows.TryGetValue(file.FileId, out var row) ? row with { RecordId = recordId, FieldId = file.FieldId, Position = file.Position }
                : new FormRecordFileRow(recordId, file.FieldId, file.FileId, file.Position, "photo.png", "image/png", 3, false)).ToArray());
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
    /// <summary>Only «Completar formularios»: whoever loads corrects their own response only before a decision.</summary>
    public bool OnlyCreate { get; set; }
    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(Allowed ? [BusinessFormsPermissions.RecordsCreate] : []);
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
    {
        var count = Interlocked.Increment(ref _checks);
        return Task.FromResult(Allowed && !(RevokeOnSecondCheck && count >= 2) && (!OnlyCreate || permission == BusinessFormsPermissions.RecordsCreate));
    }

}
