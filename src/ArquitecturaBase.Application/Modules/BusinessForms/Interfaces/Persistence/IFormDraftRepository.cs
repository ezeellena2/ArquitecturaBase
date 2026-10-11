using ArquitecturaBase.Domain.Modules.BusinessForms;
using ArquitecturaBase.Application.Modules.BusinessForms.Models;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormDraftRepository
{
    Task LockRequestAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task LockDefinitionsAsync(CancellationToken cancellationToken);
    Task<BusinessFormDefinition?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormDraftChange?> GetChangeAsync(Guid actor, Guid requestId, CancellationToken cancellationToken);
    Task<bool> ExistsPrefixAsync(string prefix, Guid exceptId, CancellationToken cancellationToken);
    Task<FormRetiredIdentities> FindRetiredIdentitiesAsync(Guid definitionId, IReadOnlyCollection<Guid> fieldIds,
        IReadOnlyCollection<Guid> choiceIds, CancellationToken cancellationToken);
    void AddRetiredIdentities(Guid definitionId, FormRetiredIdentities identities);
    void Add(BusinessFormDefinition definition);
    void AddChange(BusinessFormDraftChange change);
    Task UpdateExpiredResultsAsync(DateTime nowUtc, CancellationToken cancellationToken);
}
