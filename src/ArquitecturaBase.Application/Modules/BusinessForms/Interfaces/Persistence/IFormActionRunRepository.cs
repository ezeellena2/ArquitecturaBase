using ArquitecturaBase.Domain.Modules.BusinessForms;
namespace ArquitecturaBase.Application.Modules.BusinessForms.Interfaces.Persistence;
public interface IFormActionRunRepository
{
    Task<BusinessFormRecord?> GetRecordAsync(Guid recordId,CancellationToken cancellationToken);
    Task<BusinessFormActionRun?> GetAsync(Guid id,CancellationToken cancellationToken);
    Task<IReadOnlyList<BusinessFormActionRun>> ListForEventAsync(Guid recordId,string eventName,CancellationToken cancellationToken);
    Task<BusinessFormTask?> GetOpenTaskAsync(Guid recordId,CancellationToken cancellationToken);
    void Add(BusinessFormActionRun run);
    void AddTask(BusinessFormTask task);
}
