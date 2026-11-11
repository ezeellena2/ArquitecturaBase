namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredIssueState
{
    NotFound,
    Pending,
    Issued,
    Rejected,
    NeedsReview,
    Cancelled,
}

/// <summary>
/// A bound datum of a response, by the key its standard gives it. Value is canonical: the text as typed, an invariant decimal, a
/// date as yyyy-MM-dd or «true»/«false»; for a choice, the code of its catalog option (or else its id), with its text in Label
/// and, for a catalog option, its captured fields by key (a field that names an option of another catalog, by that option's
/// code). A block of rows brings its items, each with its bound data.
/// </summary>
public sealed record StructuredIssueDatum(string Key, string? Value, string? Label = null,
    IReadOnlyDictionary<string, string>? Fields = null, IReadOnlyList<IReadOnlyList<StructuredIssueDatum>>? Items = null)
{
    public override string ToString() => "StructuredIssueDatum { Content = [REDACTED] }";
}

/// <summary>What a run asks to issue: the bound data of its response. The run id makes it idempotent.</summary>
public sealed record StructuredIssueRequest(Guid RunId, Guid RecordId, IReadOnlyList<StructuredIssueDatum> Data)
{
    public override string ToString() => "StructuredIssueRequest { Content = [REDACTED] }";
}

/// <summary>
/// Where an issuance stands. Pending: queued, or waiting for the provider when WaitingForProvider; asked again at NextCheckAtUtc.
/// Issued: with its result and, if it has one, its private document. Rejected: refused with a stable FailureCode, and CanRetry
/// when nothing was queued. NeedsReview: a person decides, with the stable ReviewReason and the Revision the decision expects.
/// Cancelled: it will not be issued. WaitingNotice, by language, is what whoever loaded the response is told once while it waits.
/// </summary>
public sealed record StructuredIssueResponse(StructuredIssueState State, string? FailureCode = null, bool CanRetry = false,
    bool WaitingForProvider = false, DateTime? NextCheckAtUtc = null, string? ReviewReason = null, long Revision = 0,
    StructuredRecordResult? Result = null, StructuredRecordActionDocument? Document = null,
    IReadOnlyDictionary<string, string>? WaitingNotice = null)
{
    public override string ToString() => $"StructuredIssueResponse {{ State = {State}, Content = [REDACTED] }}";
}

/// <summary>
/// What would come out if the data were issued now, without saving anything: the document by language («Factura C»), the name of
/// the receiver and the total in canonical form; or a stable FailureCode when it could not be issued as it is.
/// </summary>
public sealed record StructuredIssuePreview(IReadOnlyDictionary<string, string>? Document, string? Receiver, string? Total, string? FailureCode = null)
{
    public override string ToString() => "StructuredIssuePreview { Content = [REDACTED] }";
}

/// <summary>
/// A person's decision on an issuance in review: Proceed goes on with what its reason allows (another number, today's date);
/// otherwise it is not issued.
/// </summary>
public sealed record StructuredIssueReview(long ExpectedRevision, bool Proceed, Guid ReviewedByUserId, DateTime ReviewedAtUtc)
{
    public override string ToString() => "StructuredIssueReview { Attribution = [REDACTED] }";
}
