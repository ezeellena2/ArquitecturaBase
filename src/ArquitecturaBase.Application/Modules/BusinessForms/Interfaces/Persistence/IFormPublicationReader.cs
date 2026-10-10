using ArquitecturaBase.Application.Modules.BusinessForms.Models;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormPublicationReader
{
    Task<FormPublicationStatusResponse?> FindOperationAsync(Guid definitionId, Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> ListDueIdsAsync(DateTime nowUtc, CancellationToken cancellationToken);
    Task<FormPublicationStoredRow?> FindStoredAsync(Guid operationId, CancellationToken cancellationToken);
    Task<FormPublicationStoredRow?> FindVersionAsync(Guid definitionId, Guid versionId, CancellationToken cancellationToken);
    Task<FormPublicationStoredRow?> FindActiveVersionAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<bool> ExistsOpenOperationAsync(Guid definitionId, CancellationToken cancellationToken);
}
