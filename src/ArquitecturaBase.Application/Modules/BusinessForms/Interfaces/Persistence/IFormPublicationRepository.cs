using ArquitecturaBase.Domain.Modules.BusinessForms;

namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormPublicationRepository
{
    Task<BusinessFormPublicationOperation?> GetOperationAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<BusinessFormPublicationOperation?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormVersion?> GetVersionAsync(Guid id, CancellationToken cancellationToken);
    Task<bool> ExistsOpenOperationAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<int> CountVersionsAsync(Guid definitionId, CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessFormField>> ListFieldsAsync(Guid definitionId, CancellationToken cancellationToken);
    void AddField(BusinessFormField field);
    void AddVersion(BusinessFormVersion version);
    void AddOperation(BusinessFormPublicationOperation operation);
}
