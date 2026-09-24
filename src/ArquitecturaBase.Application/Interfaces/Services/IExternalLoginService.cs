using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>Completa el ingreso tras el callback del proveedor externo.</summary>
public interface IExternalLoginService
{
    Task<Result<ExternalSignInResponse>> SignInAsync(
        ExternalSignInRequest request,
        CancellationToken cancellationToken);
}
