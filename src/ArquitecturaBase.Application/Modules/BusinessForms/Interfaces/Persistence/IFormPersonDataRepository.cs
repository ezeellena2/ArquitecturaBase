using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormPersonDataRepository
{
    Task LockDefinitionsAsync(CancellationToken cancellationToken);
    Task LockAttributesAsync(CancellationToken cancellationToken);
    Task LockDataAsync(Guid userId, CancellationToken cancellationToken);
    Task<BusinessFormPersonAttribute?> GetAttributeAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormPersonData?> GetDataAsync(Guid userId, CancellationToken cancellationToken);
    Task<List<BusinessFormPersonAttribute>> ListAttributesAsync(CancellationToken cancellationToken);
    void Add(BusinessFormPersonAttribute value);
    void Add(BusinessFormPersonData value);
}
