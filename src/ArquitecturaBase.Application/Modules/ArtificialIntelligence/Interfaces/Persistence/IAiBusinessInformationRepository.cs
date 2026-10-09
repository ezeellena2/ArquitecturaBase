using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
public interface IAiBusinessInformationRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<AiBusinessInformation?> GetAsync(CancellationToken cancellationToken);
    Task<AiBusinessInformationChange?> GetChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken);
    void Add(AiBusinessInformation information);
    void AddChange(AiBusinessInformationChange change);
    Task DeleteExpiredChangesAsync(CancellationToken cancellationToken);
}
