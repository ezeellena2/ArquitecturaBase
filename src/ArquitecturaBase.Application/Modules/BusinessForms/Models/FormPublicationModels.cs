using ArquitecturaBase.Application.Models.StructuredData;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record AcceptFormPublicationRequest(Guid RequestId, long ExpectedRevision, string CheckToken,
    IReadOnlyDictionary<string, string> Name, string Prefix, FormDraftInput Draft)
{
    public override string ToString() => "AcceptFormPublicationRequest { Content = [REDACTED] }";
}
public sealed record FormPublicationReceiptResponse(Guid RequestId, Guid OperationId, Guid DefinitionId, Guid VersionId,
    long VersionNumber, long DraftRevision, string State, DateTime AcceptedAtUtc);
public sealed record FormPublicationStatusResponse(Guid RequestId, Guid OperationId, Guid DefinitionId, Guid VersionId,
    long VersionNumber, long DraftRevision, string State, string Stage, DateTime AcceptedAtUtc, DateTime UpdatedAtUtc,
    DateTime? NextCheckAtUtc, bool CanRetry, bool RequiresReview, IReadOnlyList<FormPublicationIssueResponse>? Issues = null);
public sealed record FormVersionSchema(int Format, IReadOnlyList<CanonicalFormField> Questions)
{
    public override string ToString() => "FormVersionSchema { Content = [REDACTED] }";
}
public sealed record FormRecordStoredRow(Guid SourceReceiptId, Guid RecordId, long Number, Guid DefinitionId,
    string Prefix, Guid VersionId, long VersionNumber, string NameJson, string SchemaJson, string ValuesJson, IReadOnlyList<FormRecordReferenceRow>? References = null);
public sealed record FormRecordReferenceRow(Guid RecordId, Guid FieldId, Guid CatalogId, long CatalogRevision,
    Guid ItemId, string Text, string? Detail, string CatalogName)
{ public override string ToString() => "FormRecordReferenceRow { Content = [REDACTED] }"; }
public sealed record FormCaptureReceiptRow(Guid SourceReceiptId, Guid DefinitionId, Guid VersionId, string Fingerprint,
    Guid? RecordId, long? Number, string State, string? ErrorCode, Guid? RejectedFieldId = null)
{
    public override string ToString() => "FormCaptureReceiptRow { Receipt = [REDACTED] }";
}
public sealed record FormPublicationProfile(string ChannelId, string ProfileRevision, IReadOnlyList<FormPublicationCultureProfile> Cultures);
public sealed record FormPublicationCultureProfile(string Culture, string CompilationFingerprint);
public sealed record FormPublicationManifest(int Format, string AttemptFingerprint, string PublicationFingerprint,
    Guid DefinitionId, Guid VersionId, long DraftRevision, string ContentHash, IReadOnlyDictionary<string, string> Name,
    string Prefix, string OriginalCulture, IReadOnlyDictionary<string, string> SubmitLabel, IReadOnlyList<FormPublicationProfile> Profiles,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] int? MaxFileSizeMiB = null)
{
    public override string ToString() => "FormPublicationManifest { Content = [REDACTED] }";
}
public sealed record FormPublicationTicket(Guid Actor, Guid DefinitionId, long DraftRevision, string PublicationFingerprint, DateTime ExpiresAtUtc)
{
    public override string ToString() => "FormPublicationTicket { Content = [REDACTED] }";
}
public sealed record FormPublicationProtectionContext(Guid OperationId, Guid Actor, Guid RequestId, Guid DefinitionId, Guid VersionId);

public sealed record FormPublicationStoredRow(Guid Id, Guid Actor, Guid RequestId, Guid DefinitionId, Guid VersionId, byte[] ProtectedManifest, string SchemaJson, string State)
{
    public override string ToString() => "FormPublicationStoredRow { Content = [REDACTED] }";
}
public sealed record FormPublicationChannelRequest(string ChannelId, StructuredFormPublicationRequest Request);
public sealed record FormPublicationWorkResponse(Guid OperationId, Guid DefinitionId, Guid VersionId, Guid ActorUserId, long Fence,
    FormPublicationManifest Manifest, FormVersionSchema Schema, IReadOnlyList<FormPublicationChannelRequest> ChannelRequests,
    bool ReconciliationOnly = false)
{
    public override string ToString() => "FormPublicationWorkResponse { Content = [REDACTED] }";
}

public sealed record FormPublicationChannelProgress(string ChannelId, ArquitecturaBase.Application.Models.StructuredData.StructuredFormPublicationResponse Response);
public sealed record FormPublicationChannelDispatchResponse(IReadOnlyList<FormPublicationChannelProgress> Progress)
{
    public override string ToString() => "FormPublicationChannelDispatchResponse { Content = [REDACTED] }";
}
