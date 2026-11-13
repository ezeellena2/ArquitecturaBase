namespace ArquitecturaBase.Infrastructure.Modules.BusinessForms.Persistence;
internal static class FormAdvisoryLockKeys
{
    public static readonly string MissingRequests = "forms:missing-requests";
    public static readonly string PersonAttributes = "forms:person-attributes";
    public static string PersonData(Guid userId) => $"forms:person-data:{userId:N}";
    public static readonly string Definitions = "forms:definitions";
    // CP1-A: every folder operation, after the other forms locks.
    public static readonly string Folders = "forms:folders";
    public static string Request(Guid actor, Guid requestId) => $"forms:request:{actor:N}:{requestId:N}";
    public static string Catalog(Guid catalogId) => $"forms:catalog:{catalogId:N}";
    // A catalog a module feeds, by the key of its source: it serializes creating it, before the lock of the catalog.
    public static string CatalogKey(string key) => $"forms:catalog-key:{key}";
    public static string Capture(Guid sourceReceiptId) => $"forms:capture:{sourceReceiptId:N}";
    // Task 7: registering one channel source in the inbox, and every change to one received document (the last lock taken).
    public static string ReceivedDocumentSource(string channelId, string sourceId) => $"forms:received-document-source:{channelId}:{sourceId}";
    public static string ReceivedDocument(Guid documentId) => $"forms:received-document:{documentId:N}";
    // DOC6: asking for one request of one person, and every change to a request, before the lock of any of its documents.
    public static string DocumentProposalRequest(Guid actor, Guid requestId) => $"forms:document-proposal-request:{actor:N}:{requestId:N}";
    public static string DocumentProposal(Guid proposalId) => $"forms:document-proposal:{proposalId:N}";
    // DOC6: the reservations of one person's re-reads, their daily share, before the lock of the request.
    public static string DocumentRereadAsker(Guid actor) => $"forms:document-reread-asker:{actor:N}";
}
