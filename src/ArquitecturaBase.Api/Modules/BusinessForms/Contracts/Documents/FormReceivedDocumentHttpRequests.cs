using ArquitecturaBase.Application.Common.Pagination;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts.Documents;

/// <summary>
/// The inbox «Documentos recibidos» (DOC5-A), newest first: by form, by who sent it, by when it arrived (UTC instants, the upper one
/// exclusive), by state (<c>proposed</c>, <c>forReview</c>, <c>unclassified</c>, <c>confirmed</c> or <c>discarded</c>) and searched by the
/// numbers read (only the digits of <c>search</c> count).
/// </summary>
public sealed record ListFormReceivedDocumentsQuery(int Page = PagedRequest.DefaultPage, int PageSize = PagedRequest.DefaultPageSize, string? Sort = null,
    string? Search = null, Guid? DefinitionId = null, Guid? SenderUserId = null, DateTime? ReceivedFromUtc = null, DateTime? ReceivedToUtc = null,
    string? State = null)
{
    public override string ToString() => "ListFormReceivedDocumentsQuery { Content = [REDACTED] }";
}

/// <summary>Discarding or reopening a received document, with the revision the reviewer saw.</summary>
public sealed record ReviewFormReceivedDocumentHttpRequest(long? ExpectedRevision);

/// <summary>Changing a received document's form: the request's UUID (its repetition answers the same), the form and the revision seen.</summary>
public sealed record ChangeFormReceivedDocumentFormHttpRequest(Guid? RequestId, Guid? DefinitionId, long? ExpectedRevision);

/// <summary>Which change of form to read: the UUID of its request.</summary>
public sealed record FormReceivedDocumentExtractionQuery(Guid RequestId);

/// <summary>
/// Passing a received document to its form: the request's UUID, the version and revision the reviewer saw and the answers as the app
/// sends them (the document goes attached by the server).
/// </summary>
public sealed record PassFormReceivedDocumentHttpRequest(Guid? RequestId, Guid? VersionId, long? ExpectedRevision,
    IReadOnlyList<FormEntryAnswerHttpItem?>? Answers)
{
    public override string ToString() => "PassFormReceivedDocumentHttpRequest { Content = [REDACTED] }";
}
