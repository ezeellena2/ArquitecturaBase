using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
public interface IAiBusinessInformationReader
{
    Task<AiBusinessInformationResponse> FindAsync(CancellationToken cancellationToken);
    Task<AiBusinessInformationChangeResponse?> FindChangeAsync(Guid operatorId, Guid requestId, CancellationToken cancellationToken);
}
