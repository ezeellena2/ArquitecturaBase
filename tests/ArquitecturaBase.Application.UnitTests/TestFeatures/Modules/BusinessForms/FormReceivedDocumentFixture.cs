using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Application.Modules.BusinessForms.Services;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.BusinessForms;

// Task 7: the inbox in memory for the documents capabilities, the validation of a proposal and the capture of a received document: the
// forms with reading, catalog options by code, text or field, repetitions, documents and their history, and a reversible protector bound
// to the document and the kind, like the real one. No provider, no engine.
internal sealed class FormReceivedDocumentFixture : IFormReceivedDocumentRepository, IFormReceivedDocumentReader, IFormReceivedDocumentProtector,
    IFormCatalogItemCodeReader, IFormMissingRequestRepository
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    public List<FormDocumentFormRow> Forms { get; } = [];
    public List<FormDocumentCatalogNameRow> Catalogs { get; } = [];
    public List<(Guid CatalogId, FormDocumentCatalogItemRow Item, IReadOnlyDictionary<string, string> Fields)> Items { get; } = [];
    public List<FormDocumentRepetitionRow> Repetitions { get; } = [];
    public List<(Guid DefinitionId, string Sha256, IReadOnlyList<FormDocumentRepetitionKey> Keys)> RepetitionQueries { get; } = [];
    public List<FormReceivedDocument> Documents { get; } = [];
    public List<FormReceivedDocumentEvent> Events { get; } = [];
    public List<BusinessFormMissingTopic> Topics { get; } = [];
    public List<BusinessFormMissingRequest> MissingRequests { get; } = [];
    public List<Guid> Locks { get; } = [];

    public FormReceivedDocumentRecorder Recorder() => new(this, this, this, new FormReceivedDocumentContentPolicy(this), Clock);

    // IFormReceivedDocumentProtector: the JSON behind a marker of its document and kind; another document or kind does not read it.
    public byte[] Protect(Guid documentId, string kind, string json) => Encoding.UTF8.GetBytes($"{documentId:N}|{kind}|{json}");
    public string? Unprotect(Guid documentId, string kind, byte[] protectedValue)
    {
        var text = Encoding.UTF8.GetString(protectedValue);
        var prefix = $"{documentId:N}|{kind}|";
        return text.StartsWith(prefix, StringComparison.Ordinal) ? text[prefix.Length..] : null;
    }

    // IFormCatalogItemCodeReader
    public Task<IReadOnlyDictionary<Guid, string>> ListAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(Items.Where(item => itemIds.Contains(item.Item.ItemId)).ToDictionary(item => item.Item.ItemId, item => item.Item.Code));
    public Task<IReadOnlyDictionary<string, Guid>> ListActiveAsync(Guid catalogId, IReadOnlyCollection<string> codes, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<string, Guid>>(Items.Where(item => item.CatalogId == catalogId && codes.Contains(item.Item.Code))
            .ToDictionary(item => item.Item.Code, item => item.Item.ItemId));

    // IFormReceivedDocumentReader
    public Task<IReadOnlyList<FormDocumentFormRow>> ListReadingFormsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentFormRow>>(Forms.Where(form => !form.AppOnly && form.DocumentReadingJson is not null).Take(101).ToArray());
    // As the SQL asks it: a form published in a channel whose reading says it is on.
    public Task<bool> ExistsReadingFormAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Forms.Any(form => !form.AppOnly && FormDocumentFormPolicy.Reading(form) is { Enabled: true }));
    public Task<IReadOnlyList<FormDocumentFormNameRow>> ListReadingFormNamesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentFormNameRow>>(Forms.Where(form => !form.AppOnly && FormDocumentFormPolicy.Reading(form) is { Enabled: true })
            .Take(100).Select(form => new FormDocumentFormNameRow(form.DefinitionId, form.NameJson, form.Culture)).ToArray());
    public Task<FormDocumentFormRow?> FindActiveFormAsync(Guid definitionId, CancellationToken cancellationToken) =>
        Task.FromResult(Forms.SingleOrDefault(form => form.DefinitionId == definitionId));
    public Task<IReadOnlyList<FormDocumentCatalogNameRow>> ListCatalogNamesAsync(IReadOnlyCollection<Guid> catalogIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentCatalogNameRow>>(Catalogs.Where(catalog => catalogIds.Contains(catalog.CatalogId)).ToArray());
    public Task<FormDocumentCatalogItemRow?> FindCatalogItemAsync(Guid catalogId, string field, string value, CancellationToken cancellationToken) =>
        Task.FromResult<FormDocumentCatalogItemRow?>(Items.Where(item => item.CatalogId == catalogId).FirstOrDefault(item => field == FormDocumentLookup.Text
            ? string.Equals(item.Item.TextEs, value, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Item.TextEn, value, StringComparison.OrdinalIgnoreCase)
            : item.Fields.TryGetValue(field, out var found) && found == value).Item);
    public Task<IReadOnlyList<FormDocumentCatalogItemRow>> ListCatalogItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentCatalogItemRow>>(Items.Where(item => itemIds.Contains(item.Item.ItemId)).Select(item => item.Item).ToArray());
    public Task<IReadOnlyList<FormDocumentRepetitionRow>> ListRepetitionsAsync(Guid definitionId, string sha256, IReadOnlyList<FormDocumentRepetitionKey> keys,
        Guid? exceptDocumentId, CancellationToken cancellationToken)
    {
        RepetitionQueries.Add((definitionId, sha256, keys));
        return Task.FromResult<IReadOnlyList<FormDocumentRepetitionRow>>(Repetitions.ToArray());
    }
    public Task<FormReceivedDocumentStoredRow?> FindAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.Where(document => document.Id == documentId).Select(Row).SingleOrDefault());
    public Task<FormReceivedDocumentStoredRow?> FindBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.Where(document => document.ChannelId == channelId && document.SourceId == sourceId).Select(Row).SingleOrDefault());
    public Task<FormReceivedDocumentPageRow> FindPageAsync(ListFormReceivedDocumentsRequest request, string? searchDigits, DateTime nowUtc,
        CancellationToken cancellationToken) => throw new NotSupportedException("The inbox list is covered with PostgreSQL.");
    public Task<FormReceivedDocumentStateCounts> CountAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        throw new NotSupportedException("The inbox counts are covered with PostgreSQL.");
    public Task<IReadOnlyList<FormReceivedDocumentEventRow>> ListEventsAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormReceivedDocumentEventRow>>(Events.Where(item => item.DocumentId == documentId)
            .Select(item => new FormReceivedDocumentEventRow(item.Id, item.AtUtc, item.Kind.ToString(), item.ActorUserId, null, item.ProtectedDetail)).ToArray());
    public Task<IReadOnlyList<FormDocumentOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentOriginRow>>([]);
    public Task<IReadOnlyList<Guid>> ListExpiredIdsAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>(Documents.Where(document => document.DeleteAfterUtc <= nowUtc).Select(document => document.Id).Take(maximum).ToArray());
    public Task<IReadOnlyList<string>> ListLiveStorageKeysAsync(IReadOnlyCollection<string> storageKeys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(Documents.Select(document => document.StorageKey).OfType<string>().Where(storageKeys.Contains).Distinct().ToArray());
    public Task<IReadOnlyList<FormDocumentVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FormDocumentVersionRow>>(Forms.Where(form => versionIds.Contains(form.VersionId))
            .Select(form => new FormDocumentVersionRow(form.VersionId, form.DefinitionId, form.NameJson, form.Culture, form.SchemaJson)).ToArray());

    // IFormReceivedDocumentRepository
    public Task LockSourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task LockAsync(Guid documentId, CancellationToken cancellationToken) { Locks.Add(documentId); return Task.CompletedTask; }
    public Task<FormReceivedDocument?> GetAsync(Guid documentId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.SingleOrDefault(document => document.Id == documentId));
    public Task<FormReceivedDocument?> GetBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult(Documents.SingleOrDefault(document => document.ChannelId == channelId && document.SourceId == sourceId));
    public void Add(FormReceivedDocument document) => Documents.Add(document);
    public void Add(FormReceivedDocumentEvent documentEvent) => Events.Add(documentEvent);
    public void Remove(FormReceivedDocument document) { Documents.Remove(document); Events.RemoveAll(item => item.DocumentId == document.Id); }

    // IFormMissingRequestRepository
    public Task LockAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<BusinessFormMissingRequest?> GetRequestAsync(Guid sourceReceiptId, CancellationToken cancellationToken) =>
        Task.FromResult(MissingRequests.SingleOrDefault(request => request.SourceReceiptId == sourceReceiptId));
    Task<BusinessFormMissingTopic?> IFormMissingRequestRepository.GetAsync(Guid topicId, CancellationToken cancellationToken) =>
        Task.FromResult(Topics.SingleOrDefault(topic => topic.Id == topicId));
    public Task<BusinessFormMissingTopic?> GetByNameAsync(string normalizedName, CancellationToken cancellationToken) =>
        Task.FromResult(Topics.SingleOrDefault(topic => topic.NormalizedName == normalizedName));
    public void Add(BusinessFormMissingTopic topic) => Topics.Add(topic);
    public void Add(BusinessFormMissingRequest request) => MissingRequests.Add(request);

    /// <summary>A published form with these questions, optionally with reading on and copied from a standard with its bound data.</summary>
    public FormDocumentFormRow AddForm(IReadOnlyList<CanonicalFormField> questions, bool reading = true, string? standardKey = null,
        IReadOnlyDictionary<string, Guid>? bindings = null, IReadOnlyList<FormDocumentLookup>? lookups = null, bool appOnly = false, string name = "Comprobante")
    {
        var form = new FormDocumentFormRow(Guid.CreateVersion7(), Guid.CreateVersion7(), 3, "REC", JsonSerializer.Serialize(new Dictionary<string, string> { ["es"] = name, ["en"] = name + " (en)" }),
            "es", JsonSerializer.Serialize(new FormVersionSchema(1, questions), JsonSerializerOptions.Web),
            reading ? JsonSerializer.Serialize(new FormDocumentReading(true, "Un comprobante de transferencia.", lookups ?? []), JsonSerializerOptions.Web) : null,
            standardKey, standardKey is null ? null : 1, bindings is null ? null : JsonSerializer.Serialize(bindings, JsonSerializerOptions.Web), appOnly)
        { VersionNumber = 1, SubmitLabelJson = "{\"es\":\"Enviar\"}", QuestionCount = questions.Count };
        Forms.Add(form);
        return form;
    }

    public static CanonicalFormField Question(string type, string label, bool required = false, Guid? catalog = null, string? validation = null,
        IReadOnlyList<CanonicalFormChoice>? options = null, FormFileSettings? files = null)
    {
        var id = Guid.CreateVersion7();
        return new(id, "field_" + id.ToString("N"), type, new Dictionary<string, string> { ["es"] = label, ["en"] = label }, null, required,
            type is "shortText" or "longText" ? 120 : null, options, catalog is { } catalogId ? new FormQuestionSource(catalogId) : null, files, Validation: validation);
    }

    private FormReceivedDocumentStoredRow Row(FormReceivedDocument document) => new(document.Id, document.ChannelId, document.SourceId, document.SenderUserId, null,
        document.ReceivedAtUtc, document.StorageKey, document.FileName, document.MediaType, document.Size, document.Sha256, document.Pages,
        document.ProtectedReading, document.ProtectedProposal, document.DefinitionId, document.VersionId,
        Forms.SingleOrDefault(form => form.VersionId == document.VersionId)?.NameJson, Forms.SingleOrDefault(form => form.DefinitionId == document.DefinitionId)?.Prefix,
        document.State, document.ReasonCode, document.DocumentType, document.RecordId, null, document.ReviewedByUserId, null, document.ReviewedAtUtc,
        document.ReplyExpiresAtUtc, document.NotSendable, document.DeleteAfterUtc, document.ExtractionRequestId, document.ExtractionDefinitionId,
        document.ExtractionRequestedAtUtc, document.Revision);
}
