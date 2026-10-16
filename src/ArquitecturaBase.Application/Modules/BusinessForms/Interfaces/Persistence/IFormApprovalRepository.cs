using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;

public interface IFormApprovalRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<BusinessFormTask?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<BusinessFormRecord?> GetRecordAsync(Guid recordId, CancellationToken cancellationToken);
}
