namespace ArquitecturaBase.Application.Models.StructuredData;

public enum StructuredRecordActionState
{
    NotFound,
    Pending,
    Sending,
    Accepted,
    Failed,
    Uncertain,
}

/// <summary>Provider-independent evidence for a run and its fixed recipient. Uncertainty never authorizes a resend.</summary>
public sealed record StructuredRecordActionResponse(
    StructuredRecordActionState State, string? FailureCode = null, bool CanRetry = false,
    StructuredRecordActionReceipt? Receipt = null, StructuredRecordActionReview? Review = null);

/// <summary>Provider delivery evidence, independent from a human review.</summary>
public sealed record StructuredRecordActionReceipt(DateTime? DeliveredAtUtc, DateTime? ReadAtUtc);

/// <summary>An explicit human decision; it is not a provider delivery receipt.</summary>
public sealed record StructuredRecordActionReview(long ExpectedRevision, bool WasSent, Guid ReviewedByUserId, DateTime ReviewedAtUtc)
{
    public override string ToString() => "StructuredRecordActionReview { Attribution = [REDACTED] }";
}

/// <summary>Server-generated metadata for a private document. The content is read by the installed channel.</summary>
public sealed record StructuredRecordActionDocument(
    string StorageKey, string Name, string ContentType, long Size, string Sha256)
{
    public override string ToString() => "StructuredRecordActionDocument { Content = [REDACTED] }";
}

/// <summary>The caller fixes the recipient, language and rendered content before invoking a channel.</summary>
public sealed record StructuredRecordActionMessage(
    Guid RunId, Guid RecipientUserId, string Body, StructuredRecordActionDocument? Document = null)
{
    public override string ToString() => "StructuredRecordActionMessage { Content = [REDACTED] }";
}

/// <summary>A local observation target; absence does not authorize another send.</summary>
public sealed record StructuredRecordActionObservationRequest(Guid RunId,Guid RecipientUserId)
{ public override string ToString()=>"StructuredRecordActionObservationRequest { Target = [REDACTED] }"; }
public sealed record StructuredRecordActionObservationResponse(Guid RunId,Guid RecipientUserId,StructuredRecordActionResponse Status)
{ public override string ToString()=>"StructuredRecordActionObservationResponse { Target = [REDACTED] }"; }
