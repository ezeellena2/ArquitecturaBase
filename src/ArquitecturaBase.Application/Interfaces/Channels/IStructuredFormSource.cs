using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Channels;

public interface IStructuredFormSource
{
    Task<Result<StructuredFormClarificationResponse>> ValidateClarificationAsync(StructuredFormClarificationRequest request, CancellationToken cancellationToken);
    Task<Result<StructuredFormPrefillResponse>> ValidatePrefillAsync(StructuredFormPrefillRequest request, CancellationToken cancellationToken);
    Task<StructuredFormPreparationResponse> PrepareAsync(Guid recipientUserId, string recipientCulture,
        Guid definitionId, Guid versionId, bool forSending, CancellationToken cancellationToken);
    Task LockSelectionAsync(CancellationToken cancellationToken);
    Task<StructuredFormSelectionRow?> FindAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken);
    Task<StructuredFormSelectionRow?> FindAcceptedSessionAsync(Guid recipientUserId, string recipientCulture, Guid definitionId, Guid versionId, CancellationToken cancellationToken);
    Task<PagedResult<StructuredFormSelectionRow>> ListAsync(Guid recipientUserId, string recipientCulture,
        ListStructuredFormsRequest request, CancellationToken cancellationToken);
}
