using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>El perfil de la sesión, con sus roles, permisos y último ingreso.</summary>
public interface IProfileQueryService
{
    Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken);
}
