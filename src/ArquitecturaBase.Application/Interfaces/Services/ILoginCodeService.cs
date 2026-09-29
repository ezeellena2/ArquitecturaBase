using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Interfaces.Services;

/// <summary>
/// El ingreso con un código: pedirlo por correo y verificarlo, llegue por donde llegue (el pedido por un número lo da el
/// módulo del canal).
/// </summary>
public interface ILoginCodeService
{
    Task<Result<RequestLoginCodeResponse>> RequestLoginCodeAsync(
        RequestLoginCodeRequest request,
        CancellationToken cancellationToken);

    Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken);
}
