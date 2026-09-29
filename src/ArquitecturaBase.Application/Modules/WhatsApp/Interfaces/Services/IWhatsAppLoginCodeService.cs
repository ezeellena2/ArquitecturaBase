using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;

/// <summary>
/// El pedido de un código para entrar por WhatsApp. La verificación es la misma que la del correo
/// (<c>ILoginCodeService.VerifyLoginCodeAsync</c>): el código es del núcleo, lo que es del módulo es mandarlo.
/// </summary>
public interface IWhatsAppLoginCodeService
{
    Task<Result<RequestWhatsAppLoginCodeResponse>> RequestLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken);
}
