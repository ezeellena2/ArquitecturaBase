using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// El ingreso con un código: los dos pedidos (<see cref="SignInCodeIssuer"/>) y la verificación
/// (<see cref="LoginCodeVerifier"/>), cada uno en su límite. La cookie de la aplicación la escribe este punto de entrada,
/// después del commit.
/// </summary>
internal sealed class LoginCodeService(
    IWhatsAppAvailability whatsApp,
    SignInCodeIssuer issuer,
    LoginCodeVerifier verifier,
    ISignInService signIn,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<LoginCodeService> logger) : ILoginCodeService
{
    public Task<Result<RequestLoginCodeResponse>> RequestLoginCodeAsync(
        RequestLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<RequestLoginCodeResponse>>(logger, "RequestLoginCode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Un correo inválido, una espera o un tope de pedidos no dejan nada. El correo se encola adentro, antes del
            // commit, para que la fila se confirme ya marcada como enviada; si el commit falla, el correo sale igual, con
            // un código que no sirve.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => issuer.RequestLoginCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeAsync(
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

            // La ruta HTTP se omite cuando WhatsApp está apagado. Llegar hasta aquí es un error de programación.
            if (!whatsApp.IsEnabled)
            {
                throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
            }

            // Que la cola no tome el mensaje no es un fallo: el código se confirma sin fecha de envío.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => issuer.RequestWhatsAppLoginCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<VerifyLoginCodeResponse>>(logger, "VerifyLoginCode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => verifier.VerifyAsync(request, ct),
                // Un código equivocado cuenta el intento, un bloqueo deja su auditoría y un NotInvited o un Disabled gastan
                // el código: el error también se confirma. Una excepción igual deshace todo.
                CommitPolicy.OnAnyResult,
                cancellationToken);

            if (result.IsFailure)
            {
                return result.Error;
            }

            // La cookie de la aplicación sale recién después del commit de la cuenta, el código gastado y la auditoría,
            // como la de Google: SignInAsync lanza adentro de un límite.
            await signIn.SignInAsync(result.Value, cancellationToken);

            return new VerifyLoginCodeResponse(request.ReturnUrl!);
        });
    }
}
