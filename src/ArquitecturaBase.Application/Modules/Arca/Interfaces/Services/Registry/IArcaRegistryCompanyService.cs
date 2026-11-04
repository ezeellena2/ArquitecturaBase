using ArquitecturaBase.Application.Modules.Arca.Models.Registry;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Registry;

/// <summary>Los datos de la empresa emisora que trae el padrón.</summary>
public interface IArcaRegistryCompanyService
{
    Task<Result<ArcaCompanyResponse>> GetAsync(CancellationToken cancellationToken);

    /// <summary>Guarda o actualiza los datos de la empresa de la conexión, con el lock de la conexión.</summary>
    Task<Result> SaveAsync(Guid connectionId, ArcaRegistryPerson person, CancellationToken cancellationToken);
}
