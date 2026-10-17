using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormMissingRequestRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<BusinessFormMissingRequest?> GetRequestAsync(Guid sourceReceiptId, CancellationToken cancellationToken);
    Task<BusinessFormMissingTopic?> GetAsync(Guid topicId, CancellationToken cancellationToken);
    Task<BusinessFormMissingTopic?> GetByNameAsync(string normalizedName, CancellationToken cancellationToken);
    void Add(BusinessFormMissingTopic topic);
    void Add(BusinessFormMissingRequest request);
}
