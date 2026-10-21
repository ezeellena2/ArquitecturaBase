namespace ArquitecturaBase.Application.Modules.BusinessForms.Models;

public sealed record DeactivateFormRequest(long ExpectedRevision);
public sealed record DeleteFormRequest(long ExpectedRevision);
public sealed record FormDefinitionLifecycleResponse(long Revision, DateTime? DeactivatedAtUtc, FormAuthorResponse? DeactivatedBy,
    long? ResponseCount, int? PendingApprovalCount, string? NextRecordNumber, long NextVersionNumber,
    bool CanDeactivate, bool CanReactivate, string? UnavailableReason,
    DateTime? LastOpenSessionExpiresAtUtc, bool CanDelete, string? DeletionUnavailableReason);
public sealed record FormIncomingSuggestionResponse(Guid DefinitionId, IReadOnlyDictionary<string, string> Name)
{
    public override string ToString() => "FormIncomingSuggestionResponse { Content = [REDACTED] }";
}
public sealed record FormDeactivationPreviewResponse(Guid DefinitionId, long Revision, FormDefinitionLifecycleResponse Lifecycle,
    IReadOnlyList<FormIncomingSuggestionResponse> AffectedSuggestions);
public sealed record FormDeactivationResponse(Guid DefinitionId, long Revision, FormDefinitionLifecycleResponse Lifecycle);
public sealed record FormLifecycleStoredRow(Guid DefinitionId, long Revision, string Prefix, Guid? ActiveVersionId,
    long NextVersionNumber, long LastRecordNumber, int PendingApprovalCount, DateTime? DeactivatedAtUtc,
    FormAuthorResponse? DeactivatedBy, bool PublicationInProgress);
