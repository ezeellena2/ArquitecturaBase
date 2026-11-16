using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
public interface IAiKeyRingControlRepository
{
    Task LockAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<AiKeyRingControl>> ListAsync(CancellationToken cancellationToken);
    void Add(AiKeyRingControl control);
}
