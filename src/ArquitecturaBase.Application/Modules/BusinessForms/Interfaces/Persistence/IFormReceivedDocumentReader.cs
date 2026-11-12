using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

/// <summary>
/// Reads for the documents capabilities and the inbox «Documentos recibidos», without tracking: the forms a document can be read for,
/// the catalog options a reading fills, the repetitions of a document, the inbox with its filters, search and counts, a document with
/// its history, the origin of the responses passed from it and the copies of the live documents. Fixed queries, bounded in SQL.
/// </summary>
public interface IFormReceivedDocumentReader
{
    /// <summary>Active versions published in a channel whose sealed reading exists (the caller checks it is on), up to 101.</summary>
    Task<IReadOnlyList<FormDocumentFormRow>> ListReadingFormsAsync(CancellationToken cancellationToken);
    /// <summary>The active published version of a form, in a channel or only in the app, or null.</summary>
    Task<FormDocumentFormRow?> FindActiveFormAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormDocumentCatalogNameRow>> ListCatalogNamesAsync(IReadOnlyCollection<Guid> catalogIds, CancellationToken cancellationToken);
    /// <summary>
    /// The first active option of a catalog whose text (either language, ignoring case) or whose field (its id in "D" format) equals the
    /// value, or null; options by code are read with <see cref="IFormCatalogItemCodeReader"/>.
    /// </summary>
    Task<FormDocumentCatalogItemRow?> FindCatalogItemAsync(Guid catalogId, string field, string value, CancellationToken cancellationToken);
    /// <summary>The texts of the given options of a catalog, by id.</summary>
    Task<IReadOnlyList<FormDocumentCatalogItemRow>> ListCatalogItemsAsync(IReadOnlyCollection<Guid> itemIds, CancellationToken cancellationToken);
    /// <summary>
    /// Where the same file or the same data already are in a form: its current responses with a file of that SHA-256 or with all the
    /// given values, and the inbox documents of the form with that SHA-256 that were not discarded, the earliest first, at most two.
    /// </summary>
    Task<IReadOnlyList<FormDocumentRepetitionRow>> ListRepetitionsAsync(Guid definitionId, string sha256, IReadOnlyList<FormDocumentRepetitionKey> keys,
        Guid? exceptDocumentId, CancellationToken cancellationToken);
    Task<FormReceivedDocumentStoredRow?> FindAsync(Guid documentId, CancellationToken cancellationToken);
    Task<FormReceivedDocumentStoredRow?> FindBySourceAsync(string channelId, string sourceId, CancellationToken cancellationToken);
    Task<FormReceivedDocumentPageRow> FindPageAsync(ListFormReceivedDocumentsRequest request, string? searchDigits, DateTime nowUtc,
        CancellationToken cancellationToken);
    Task<FormReceivedDocumentStateCounts> CountAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<FormReceivedDocumentEventRow>> ListEventsAsync(Guid documentId, CancellationToken cancellationToken);
    /// <summary>The origin of the responses a reviewer passed from the inbox, by their receipts.</summary>
    Task<IReadOnlyList<FormDocumentOriginRow>> ListOriginsAsync(IReadOnlyCollection<Guid> sourceReceiptIds, CancellationToken cancellationToken);
    /// <summary>The discarded or unclassified documents whose deletion date passed, the oldest first, up to <paramref name="maximum"/>.</summary>
    Task<IReadOnlyList<Guid>> ListExpiredIdsAsync(DateTime nowUtc, int maximum, CancellationToken cancellationToken);
    /// <summary>Of the given copies, those a document still in the inbox keeps.</summary>
    Task<IReadOnlyList<string>> ListLiveStorageKeysAsync(IReadOnlyCollection<string> storageKeys, CancellationToken cancellationToken);
    /// <summary>The sealed name, culture and schema of the given versions.</summary>
    Task<IReadOnlyList<FormDocumentVersionRow>> ListVersionsAsync(IReadOnlyCollection<Guid> versionIds, CancellationToken cancellationToken);
}
