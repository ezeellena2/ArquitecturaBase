using ArquitecturaBase.Application.Modules.Arca.Models.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;

public interface IArcaRegistryReader
{
    Task<ArcaRegistryCompanyRow?> FindCompanyAsync(Guid connectionId, CancellationToken cancellationToken);
}
