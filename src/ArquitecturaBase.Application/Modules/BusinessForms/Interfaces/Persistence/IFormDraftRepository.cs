using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormDraftRepository
{
    Task LockRequestAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task LockDefinitionsAsync(CancellationToken cancellationToken);
    Task<BusinessFormDefinition?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<bool> ExistsPrefixAsync(string prefix, Guid exceptId, CancellationToken cancellationToken);
    void Add(BusinessFormDefinition definition);
    void AddChange(BusinessFormDraftChange change);
    Task UpdateExpiredResultsAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
