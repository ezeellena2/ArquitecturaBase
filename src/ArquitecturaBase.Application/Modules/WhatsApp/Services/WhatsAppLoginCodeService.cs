using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El pedido de un código para entrar por WhatsApp, en su límite: lo emite y lo encola <see cref="WhatsAppCodeIssuer"/>.
/// La verificación es la del correo, en el núcleo (LoginCodeService), que también escribe la cookie.
/// </summary>
internal sealed class WhatsAppLoginCodeService(
    WhatsAppCodeIssuer issuer,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<WhatsAppLoginCodeService> logger) : IWhatsAppLoginCodeService
{
    public Task<Result<RequestWhatsAppLoginCodeResponse>> RequestLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<RequestWhatsAppLoginCodeResponse>>(logger, "RequestWhatsAppLoginCode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            issuer.EnsureEnabledForSignIn();

            // Un número inválido, un país sin WhatsApp, el tope diario o un límite del número no dejan nada. Que la cola
            // no tome el mensaje no es un fallo: el código se confirma sin fecha de envío.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => issuer.RequestSignInCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }
}
