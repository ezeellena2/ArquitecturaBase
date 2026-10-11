using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Api.Modules.BusinessForms.Contracts;

public sealed record FormChangeRecoveryHttpResponse(string Path, DateTime ResultExpiresAtUtc);
public sealed record FormDraftCommitHttpResponse(Guid RequestId, FormDefinitionResponse Definition, FormIdentityMap IdentityMap,
    FormChangeRecoveryHttpResponse Recovery);
public sealed record FormRecoveredChangeHttpResponse(Guid RequestId, string Kind, string Status, DateTime CommittedAtUtc,
    long CurrentRevision, FormDraftCommitHttpResponse Result);
public sealed record FormCurrentPublicationHttpResponse(Guid VersionId, long VersionNumber, long DraftRevision, string State,
    string Stage, bool RequiresReview, bool CanRetry, Guid? RequestId, string? RecoveryPath);
public sealed record FormPublicationOverviewHttpResponse(FormActiveVersionResponse? ActiveVersion,
    FormCurrentPublicationHttpResponse? CurrentOperation, string DisplayState);
public sealed record FormDefinitionDetailHttpResponse(FormDefinitionResponse Definition, FormCapabilitiesResponse Capabilities,
    FormAuditResponse Audit, FormPublicationOverviewHttpResponse Publication);
