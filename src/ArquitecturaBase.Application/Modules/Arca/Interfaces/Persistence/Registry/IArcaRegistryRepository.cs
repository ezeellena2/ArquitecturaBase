using ArquitecturaBase.Domain.Modules.Arca.Registry;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Persistence.Registry;

/// <summary>Los datos de la empresa que trajo el padrón. Quien escribe toma antes el lock de la conexión.</summary>
public interface IArcaRegistryRepository
{
    Task<ArcaRegistryCompany?> GetCompanyAsync(Guid connectionId, CancellationToken cancellationToken);
    void Add(ArcaRegistryCompany company);
}
