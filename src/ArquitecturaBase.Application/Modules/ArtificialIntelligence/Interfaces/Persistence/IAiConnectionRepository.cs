using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
public interface IAiConnectionRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<AiConnection?> GetAsync(CancellationToken cancellationToken);
    Task<AiConnectionChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken);
    void Add(AiConnection connection);
    void AddChange(AiConnectionChange change);
    Task DeleteExpiredChangesAsync(CancellationToken cancellationToken);
}
