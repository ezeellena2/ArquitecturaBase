using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
using ArquitecturaBase.Domain.Files;
using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Domain.Modules.BusinessForms.Documents;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence.Readers;

// Task 7: the reads of the documents capabilities and of the inbox «Documentos recibidos», without tracking and filtered in SQL before
// ordering: the forms with reading (the channel's active versions, or any active published version for the fixed form), the catalog
// options a reading fills, the repetitions of a document in a form, the inbox with its filters, number search, counts and the forms and
// senders to filter by (each counted under the other filters, at most 100 with the selected one always in, marking the cut), a document
// with its history, the origin of the responses passed from it (with the document's file name) and the copies the live documents keep.
internal sealed class FormReceivedDocumentReader(ApplicationDbContext dbContext) : IFormReceivedDocumentReader
{
    private const int MaxForms = 101;
    private const int MaxFormNames = 100;
    private const int MaxEvents = 200;
    private const int MaxFacets = 100;
    private const string ReadingOn = """{"enabled":true}""";
    private static readonly IReadOnlyDictionary<string, Expression<Func<FormReceivedDocument, object?>>> Sortable =
        new Dictionary<string, Expression<Func<FormReceivedDocument, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["receivedAtUtc"] = document => document.ReceivedAtUtc,
        };

    public async Task<IReadOnlyList<FormDocumentFormRow>> ListReadingFormsAsync(CancellationToken cancellationToken) =>
        await Forms(FormOfferableVersionQuery.ChannelVersions(dbContext).Where(version => version.DocumentReadingJson != null))
            .Take(MaxForms).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormDocumentFormNameRow>> ListReadingFormNamesAsync(CancellationToken cancellationToken) =>
        await (from version in FormOfferableVersionQuery.ChannelVersions(dbContext)
               where version.DocumentReadingJson != null && EF.Functions.JsonContains(version.DocumentReadingJson, ReadingOn)
               join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on version.DefinitionId equals definition.Id
               orderby definition.Id
               select new FormDocumentFormNameRow(definition.Id, version.NameJson!, version.Culture!)).Take(MaxFormNames).ToListAsync(cancellationToken);

    public Task<FormDocumentFormRow?> FindActiveFormAsync(Guid definitionId, CancellationToken cancellationToken) =>
        Forms(FormOfferableVersionQuery.Versions(dbContext).Where(version => version.DefinitionId == definitionId)).SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<FormDocumentCatalogNameRow>> ListCatalogNamesAsync(IReadOnlyCollection<Guid> catalogIds, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalogIds.Contains(catalog.Id))
            .Select(catalog => new FormDocumentCatalogNameRow(catalog.Id, catalog.NameJson, catalog.OriginalCulture)).ToListAsync(cancellationToken);

    public async Task<FormDocumentCatalogItemRow?> FindCatalogItemAsync(Guid catalogId, string field, string value, CancellationToken cancellationToken)
    {
        var items = dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => item.CatalogId == catalogId && item.IsActive);
        if (field == FormDocumentLookup.Text)
        {
            var text = LikePatterns.Exact(value.Trim());
            items = items.Where(item => item.TextEs != null && EF.Functions.ILike(item.TextEs, text, LikePatterns.EscapeCharacter)
                || item.TextEn != null && EF.Functions.ILike(item.TextEn, text, LikePatterns.EscapeCharacter));
        }
        else
        {
            if (!Guid.TryParseExact(field, "D", out var fieldId)) return null;
            var fieldsJson = await dbContext.Set<BusinessFormCatalog>().AsNoTracking().Where(catalog => catalog.Id == catalogId)
                .Select(catalog => catalog.FieldsJson).SingleOrDefaultAsync(cancellationToken);
            if (fieldsJson is null || JsonSerializer.Deserialize<BusinessFormCatalogField[]>(fieldsJson, JsonSerializerOptions.Web)?
                    .Any(item => item.Id == fieldId && item.IsActive) != true) return null;
            // An option keeps its field values by the field's Id, as every catalog filter compares them.
            var containment = JsonSerializer.Serialize(new Dictionary<string, string> { [fieldId.ToString("D")] = value.Trim() });
            items = items.Where(item => EF.Functions.JsonContains(item.FieldValuesJson, containment));
        }
        return await items.OrderBy(item => item.Position).ThenBy(item => item.Id)
            .Select(item => new FormDocumentCatalogItemRow(item.Id, item.Code, item.TextEs, item.TextEn)).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FormDocumentCatalogItemRow>> ListCatalogItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormCatalogItem>().AsNoTracking().Where(item => itemIds.Contains(item.Id))
            .Select(item => new FormDocumentCatalogItemRow(item.Id, item.Code, item.TextEs, item.TextEn)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormDocumentRepetitionRow>> ListRepetitionsAsync(Guid definitionId, string sha256, IReadOnlyList<FormDocumentRepetitionKey> keys,
        Guid? exceptDocumentId, CancellationToken cancellationToken)
    {
        var current = dbContext.Set<BusinessFormRecord>().AsNoTracking()
            .Where(record => record.DefinitionId == definitionId && record.ReplacedByRecordId == null && record.State != FormRecordState.Voided);
        var found = new List<FormDocumentRepetitionRow>();
        var byFile = await (from record in current
                            join file in dbContext.Set<BusinessFormRecordFile>().AsNoTracking() on record.Id equals file.RecordId
                            join stored in dbContext.Set<StoredFile>().AsNoTracking() on file.FileId equals stored.Id
                            where stored.Sha256 == sha256
                            orderby record.SubmittedAtUtc, record.Id
                            select new FormDocumentRepetitionRow(record.SubmittedAtUtc, record.Id, record.Number, null)).FirstOrDefaultAsync(cancellationToken);
        if (byFile is not null) found.Add(byFile);
        if (keys.Count > 0)
        {
            var byKeys = current;
            foreach (var key in keys) byKeys = byKeys.Where(Matches(key));
            var match = await byKeys.OrderBy(record => record.SubmittedAtUtc).ThenBy(record => record.Id)
                .Select(record => new FormDocumentRepetitionRow(record.SubmittedAtUtc, record.Id, record.Number, null)).FirstOrDefaultAsync(cancellationToken);
            if (match is not null) found.Add(match);
        }
        var inInbox = await dbContext.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.DefinitionId == definitionId && document.Sha256 == sha256 && document.State != FormReceivedDocumentState.Discarded
                && (exceptDocumentId == null || document.Id != exceptDocumentId))
            .OrderBy(document => document.ReceivedAtUtc).ThenBy(document => document.Id)
            .Select(document => new FormDocumentRepetitionRow(document.ReceivedAtUtc, document.RecordId,
                dbContext.Set<BusinessFormRecord>().Where(record => record.Id == document.RecordId).Select(record => (long?)record.Number).FirstOrDefault(),
                document.Id)).FirstOrDefaultAsync(cancellationToken);
        if (inInbox is not null) found.Add(inInbox);
        return found.OrderBy(row => row.AtUtc).Take(2).ToArray();
    }

    public Task<FormReceivedDocumentStoredRow?> FindAsync(Guid documentId, CancellationToken cancellationToken) =>
        Rows(dbContext.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.Id == documentId)).SingleOrDefaultAsync(cancellationToken);

    public Task<FormReceivedDocumentStoredRow?> FindBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken) =>
        Rows(dbContext.Set<FormReceivedDocument>().AsNoTracking().Where(document => document.ChannelId == channelId && document.SourceId == sourceId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<FormReceivedDocumentPageRow> FindPageAsync(ListFormReceivedDocumentsRequest request, string? searchDigits, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var documents = dbContext.Set<FormReceivedDocument>().AsNoTracking();
        if (request.ReceivedFromUtc is { } from) documents = documents.Where(document => document.ReceivedAtUtc >= from);
        if (request.ReceivedToUtc is { } to) documents = documents.Where(document => document.ReceivedAtUtc < to);
        if (searchDigits is not null)
        {
            // Only digits reach the query, as the prefix of one of the numbers kept.
            var query = searchDigits + ":*";
            documents = documents.Where(document => EF.Functions.ToTsVector("simple", document.SearchText).Matches(EF.Functions.ToTsQuery("simple", query)));
        }
        // The counts per state, the forms and the senders are each counted under the other filters, never their own.
        var counts = await CountAsync(OfSender(OfForm(documents, request), request), nowUtc, cancellationToken);
        var (forms, formsTruncated) = await FormFacetsAsync(InState(OfSender(documents, request), request.State, nowUtc), request.DefinitionId, cancellationToken);
        var (senders, sendersTruncated) = await SenderFacetsAsync(InState(OfForm(documents, request), request.State, nowUtc), request.SenderUserId,
            cancellationToken);
        var filtered = InState(OfSender(OfForm(documents, request), request), request.State, nowUtc);
        var page = await Rows(filtered.ApplySort(SortDescriptor.Parse(request.Sort), Sortable, new("receivedAtUtc", true), document => document.Id))
            .ToPagedResultAsync(request, cancellationToken);
        return new FormReceivedDocumentPageRow(page.Items, page.TotalCount, counts, forms, formsTruncated, senders, sendersTruncated);
    }

    public Task<FormReceivedDocumentStateCounts> CountAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        CountAsync(dbContext.Set<FormReceivedDocument>().AsNoTracking(), nowUtc, cancellationToken);

    // The latest steps of a document's history, oldest first: a history that grows (reopened, read again) never makes the detail unbounded.
    public async Task<IReadOnlyList<FormReceivedDocumentEventRow>> ListEventsAsync(Guid documentId, CancellationToken cancellationToken) =>
        (await dbContext.Set<FormReceivedDocumentEvent>().AsNoTracking().Where(item => item.DocumentId == documentId)
            .OrderByDescending(item => item.AtUtc).ThenByDescending(item => item.Id).Take(MaxEvents)
            .Select(item => new FormReceivedDocumentEventRow(item.Id, item.AtUtc, item.Kind.ToString(), item.ActorUserId,
                dbContext.Users.Where(user => user.Id == item.ActorUserId).Select(user => user.DisplayName).FirstOrDefault(), item.ProtectedDetail))
            .ToListAsync(cancellationToken)).OrderBy(item => item.AtUtc).ThenBy(item => item.Id).ToArray();

    public async Task<IReadOnlyList<FormDocumentOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken) =>
        await (from document in dbContext.Set<FormReceivedDocument>().AsNoTracking()
               join record in dbContext.Set<BusinessFormRecord>().AsNoTracking() on document.RecordId equals record.Id
               where document.ReviewedByUserId != null && document.ReviewedAtUtc != null && sourceReceiptIds.Contains(record.SourceReceiptId)
               select new FormDocumentOriginRow(record.SourceReceiptId, document.ReviewedByUserId!.Value,
                   dbContext.Users.Where(user => user.Id == document.ReviewedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
                   document.ReviewedAtUtc!.Value, document.Id, document.FileName)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListExpiredIdsAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken) =>
        await dbContext.Set<FormReceivedDocument>().AsNoTracking()
            .Where(document => document.DeleteAfterUtc != null && document.DeleteAfterUtc <= nowUtc
                && (document.State == FormReceivedDocumentState.Discarded || document.State == FormReceivedDocumentState.Unclassified))
            .OrderBy(document => document.DeleteAfterUtc).ThenBy(document => document.Id).Select(document => document.Id).Take(maximum)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> ListLiveStorageKeysAsync(IReadOnlyCollection<string> storageKeys, CancellationToken cancellationToken) =>
        await dbContext.Set<FormReceivedDocument>().AsNoTracking().Where(document => storageKeys.Contains(document.StorageKey))
            .Select(document => document.StorageKey).Distinct().ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<FormDocumentVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken) =>
        await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => versionIds.Contains(version.Id) && version.NameJson != null && version.Culture != null)
            .Select(version => new FormDocumentVersionRow(version.Id, version.DefinitionId, version.NameJson!, version.Culture!, version.SchemaJson))
            .ToListAsync(cancellationToken);

    private IQueryable<FormDocumentFormRow> Forms(IQueryable<BusinessFormVersion> versions) =>
        from version in versions
        join definition in dbContext.Set<BusinessFormDefinition>().AsNoTracking() on version.DefinitionId equals definition.Id
        orderby definition.Id
        select new FormDocumentFormRow(definition.Id, version.Id, definition.Revision, definition.Prefix, version.NameJson!, version.Culture!,
            version.SchemaJson, version.DocumentReadingJson, definition.StandardKey, definition.StandardVersion, definition.StandardBindingsJson, version.AppOnly)
        {
            VersionNumber = version.Number, SubmitLabelJson = version.SubmitLabelJson!, QuestionCount = version.QuestionCount!.Value,
        };

    private IQueryable<FormReceivedDocumentStoredRow> Rows(IQueryable<FormReceivedDocument> documents) =>
        documents.Select(document => new FormReceivedDocumentStoredRow(document.Id, document.ChannelId, document.SourceId, document.SenderUserId,
            dbContext.Users.Where(user => user.Id == document.SenderUserId).Select(user => user.DisplayName).FirstOrDefault(),
            document.ReceivedAtUtc, document.StorageKey, document.FileName, document.MediaType, document.Size, document.Sha256, document.Pages,
            document.ProtectedReading, document.ProtectedProposal, document.DefinitionId, document.VersionId,
            dbContext.Set<BusinessFormVersion>().Where(version => version.Id == document.VersionId).Select(version => version.NameJson).FirstOrDefault(),
            dbContext.Set<BusinessFormDefinition>().Where(definition => definition.Id == document.DefinitionId).Select(definition => definition.Prefix).FirstOrDefault(),
            document.State, document.ReasonCode, document.DocumentType, document.RecordId,
            dbContext.Set<BusinessFormRecord>().Where(record => record.Id == document.RecordId).Select(record => (long?)record.Number).FirstOrDefault(),
            document.ReviewedByUserId, dbContext.Users.Where(user => user.Id == document.ReviewedByUserId).Select(user => user.DisplayName).FirstOrDefault(),
            document.ReviewedAtUtc, document.ReplyExpiresAtUtc, document.NotSendable, document.DeleteAfterUtc, document.ExtractionRequestId,
            document.ExtractionDefinitionId, document.ExtractionRequestedAtUtc, document.Revision)
        {
            ExtractionActorUserId = document.ExtractionActorUserId, ExtractionCulture = document.ExtractionCulture,
            ExtractionTimeZoneId = document.ExtractionTimeZoneId,
        });

    private static async Task<FormReceivedDocumentStateCounts> CountAsync(IQueryable<FormReceivedDocument> documents, DateTime nowUtc, CancellationToken ct)
    {
        var counts = await documents.GroupBy(document => 1).Select(group => new
        {
            Proposed = group.Count(document => document.State == FormReceivedDocumentState.Proposed),
            ForReview = group.Count(document => document.State == FormReceivedDocumentState.Proposed
                && (document.NotSendable || document.ReplyExpiresAtUtc == null || document.ReplyExpiresAtUtc <= nowUtc)),
            Unclassified = group.Count(document => document.State == FormReceivedDocumentState.Unclassified),
            Confirmed = group.Count(document => document.State == FormReceivedDocumentState.Confirmed),
            Discarded = group.Count(document => document.State == FormReceivedDocumentState.Discarded),
        }).FirstOrDefaultAsync(ct);
        return counts is null ? new(0, 0, 0, 0, 0) : new(counts.Proposed, counts.ForReview, counts.Unclassified, counts.Confirmed, counts.Discarded);
    }

    private static Expression<Func<FormReceivedDocument, bool>> ForReview(DateTime nowUtc) => document => document.State == FormReceivedDocumentState.Proposed
        && (document.NotSendable || document.ReplyExpiresAtUtc == null || document.ReplyExpiresAtUtc <= nowUtc);

    private static IQueryable<FormReceivedDocument> OfForm(IQueryable<FormReceivedDocument> documents, ListFormReceivedDocumentsRequest request) =>
        request.DefinitionId is { } definitionId ? documents.Where(document => document.DefinitionId == definitionId) : documents;

    private static IQueryable<FormReceivedDocument> OfSender(IQueryable<FormReceivedDocument> documents, ListFormReceivedDocumentsRequest request) =>
        request.SenderUserId is { } senderId ? documents.Where(document => document.SenderUserId == senderId) : documents;

    private static IQueryable<FormReceivedDocument> InState(IQueryable<FormReceivedDocument> documents, string? state, DateTime nowUtc) => state switch
    {
        "proposed" => documents.Where(document => document.State == FormReceivedDocumentState.Proposed),
        "forReview" => documents.Where(ForReview(nowUtc)),
        "unclassified" => documents.Where(document => document.State == FormReceivedDocumentState.Unclassified),
        "confirmed" => documents.Where(document => document.State == FormReceivedDocumentState.Confirmed),
        "discarded" => documents.Where(document => document.State == FormReceivedDocumentState.Discarded),
        _ => documents,
    };

    // The forms with documents, the most first, one row each, by the name of its active version or, for one without it, of its documents'.
    private async Task<(IReadOnlyList<FormReceivedDocumentFormFacetRow> Rows, bool Truncated)> FormFacetsAsync(IQueryable<FormReceivedDocument> documents,
        Guid? selected, CancellationToken ct)
    {
        var (counted, truncated) = await FacetAsync(documents.Where(document => document.DefinitionId != null), document => document.DefinitionId!.Value,
            selected, ct);
        if (counted.Count == 0) return ([], truncated);
        var ids = counted.Select(row => row.Key).ToArray();
        var names = await dbContext.Set<BusinessFormDefinition>().AsNoTracking().Where(definition => ids.Contains(definition.Id))
            .Select(definition => new
            {
                definition.Id,
                NameJson = dbContext.Set<BusinessFormVersion>().Where(version => version.Id == definition.ActiveVersionId).Select(version => version.NameJson).FirstOrDefault(),
            }).ToDictionaryAsync(row => row.Id, row => row.NameJson, ct);
        var missing = ids.Where(id => names.GetValueOrDefault(id) is null).ToArray();
        if (missing.Length > 0)
        {
            var versionIds = dbContext.Set<FormReceivedDocument>()
                .Where(document => document.DefinitionId != null && missing.Contains(document.DefinitionId.Value) && document.VersionId != null)
                .Select(document => document.VersionId!.Value);
            var versions = await dbContext.Set<BusinessFormVersion>().AsNoTracking().Where(version => versionIds.Contains(version.Id) && version.NameJson != null)
                .Select(version => new { version.DefinitionId, version.Number, version.NameJson }).ToListAsync(ct);
            foreach (var version in versions.GroupBy(row => row.DefinitionId))
                names[version.Key] = version.MaxBy(row => row.Number)!.NameJson;
        }
        return (counted.Select(row => new FormReceivedDocumentFormFacetRow(row.Key, names.GetValueOrDefault(row.Key), row.Count)).ToArray(), truncated);
    }

    // The people who sent documents, the most first, by the name of their account.
    private async Task<(IReadOnlyList<FormReceivedDocumentSenderFacetRow> Rows, bool Truncated)> SenderFacetsAsync(IQueryable<FormReceivedDocument> documents,
        Guid? selected, CancellationToken ct)
    {
        var (counted, truncated) = await FacetAsync(documents, document => document.SenderUserId, selected, ct);
        if (counted.Count == 0) return ([], truncated);
        var ids = counted.Select(row => row.Key).ToArray();
        var names = await dbContext.Users.Where(user => ids.Contains(user.Id)).Select(user => new { user.Id, user.DisplayName })
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, ct);
        return (counted.Select(row => new FormReceivedDocumentSenderFacetRow(row.Key, names.GetValueOrDefault(row.Key), row.Count)).ToArray(), truncated);
    }

    // A facet: the first MaxFacets keys with the most documents, reading one more to know it cuts, and always the selected key with its count
    // under the same filters (even zero), in the place its count gives it; when it takes the place of the last one, that one is cut.
    private static async Task<(List<(Guid Key, int Count)> Rows, bool Truncated)> FacetAsync(IQueryable<FormReceivedDocument> documents,
        Expression<Func<FormReceivedDocument, Guid>> key, Guid? selected, CancellationToken ct)
    {
        var read = await documents.GroupBy(key).Select(group => new { group.Key, Count = group.Count() })
            .OrderByDescending(group => group.Count).ThenBy(group => group.Key).Take(MaxFacets + 1).ToListAsync(ct);
        var rows = read.Take(MaxFacets).Select(group => (group.Key, group.Count)).ToList();
        var truncated = read.Count > MaxFacets;
        if (selected is { } chosen && rows.TrueForAll(row => row.Key != chosen))
        {
            var count = read.FirstOrDefault(group => group.Key == chosen)?.Count ?? await documents.CountAsync(KeyEquals(key, chosen), ct);
            if (rows.Count == MaxFacets)
            {
                rows.RemoveAt(rows.Count - 1);
                truncated = true;
            }
            var place = rows.FindIndex(row => row.Count < count);
            rows.Insert(place < 0 ? rows.Count : place, (chosen, count));
        }
        return (rows, truncated);
    }

    private static Expression<Func<FormReceivedDocument, bool>> KeyEquals(Expression<Func<FormReceivedDocument, Guid>> key, Guid value)
    {
        Expression<Func<Guid>> parameter = () => value;
        return Expression.Lambda<Func<FormReceivedDocument, bool>>(Expression.Equal(key.Body, parameter.Body), key.Parameters);
    }

    // A duplicate key compares the typed value its question keeps.
    private Expression<Func<BusinessFormRecord, bool>> Matches(FormDocumentRepetitionKey key)
    {
        var values = dbContext.Set<BusinessFormRecordValue>();
        var fieldId = key.FieldId;
        switch (key.Type)
        {
            case "decimal":
                var number = decimal.Parse(key.Value, NumberStyles.Number, CultureInfo.InvariantCulture);
                return record => values.Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.DecimalValue == number);
            case "date":
                var date = DateOnly.ParseExact(key.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                return record => values.Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.DateValue == date);
            default:
                var text = key.Value;
                return record => values.Any(value => value.RecordId == record.Id && value.FieldId == fieldId && value.TextValue == text);
        }
    }
}
