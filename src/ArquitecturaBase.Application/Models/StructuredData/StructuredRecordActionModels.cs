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
    StructuredRecordActionState State, string? FailureCode = null, bool CanRetry = false);

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
