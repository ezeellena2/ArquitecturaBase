using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
public interface IAiConnectionReader
{
    Task<AiConnectionSnapshot> FindAsync(CancellationToken cancellationToken);
    Task<AiConnectionChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken);
}
