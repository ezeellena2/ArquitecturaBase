using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class LoginLinkService(
    LoginLinkVerifier verifier,
    ISignInService signIn,
    IPhoneNumberParser phoneNumbers,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<LoginLinkService> logger) : ILoginLinkService
{
    // OperationLog registra solo la operación y el código de error: el token y la URL nunca van al log. La prueba de
    // privacidad de los enlaces (LoginLinkTests) busca "Handling PreviewLoginLink" y "Handling RedeemLoginLink" para
    // confirmar que el log se capturó.
    public Task<Result<LoginLinkPreviewResponse>> PreviewAsync(
        PreviewLoginLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<LoginLinkPreviewResponse>>(logger, "PreviewLoginLink", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var user = await verifier.PreviewAsync(request, cancellationToken);
            if (user.IsFailure)
            {
                return user.Error;
            }

            return new LoginLinkPreviewResponse(user.Value.DisplayName ?? user.Value.Email, MaskedPhoneOf(user.Value));
        });
    }

    private string? MaskedPhoneOf(UserAccount user)
    {
        var phone = PhoneNumber.Create(user.PhoneNumber);
        return phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null;
    }

    public Task<Result> RedeemAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "RedeemLoginLink", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            var result = await unitOfWork.ExecuteInTransactionAsync(
                ct => verifier.RedeemAsync(request, ct),
                // El enlace queda consumido aunque la cuenta esté bloqueada, deshabilitada o borrada, y todo intento sobre
                // una cuenta existente deja su auditoría: el error también se confirma. Una excepción igual deshace todo.
                CommitPolicy.OnAnyResult,
                cancellationToken);

            if (result.IsFailure)
            {
                return result.Error;
            }

            // La cookie de la aplicación sale recién después del commit del enlace gastado y la auditoría, como en los
            // otros dos ingresos: SignInAsync lanza adentro de un límite.
            await signIn.SignInAsync(result.Value, cancellationToken);

            return Result.Success();
        });
    }
}
