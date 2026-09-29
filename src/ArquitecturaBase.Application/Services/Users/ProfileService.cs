using ArquitecturaBase.Application.Common.Exceptions;
using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Los datos del perfil y su correo: editar nombre, idioma y zona horaria, pedir un código para un correo y
/// confirmarlo. El código lo emite <see cref="DestinationCodeIssuer"/> y lo verifica <see cref="DestinationCodeVerifier"/>.
/// No cambian la sesión actual. La lectura está en <see cref="ProfileQueryService"/>, y el WhatsApp propio, en
/// <c>ProfileWhatsAppService</c>.
/// </summary>
internal sealed class ProfileService(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    DestinationCodeIssuer issuer,
    DestinationCodeVerifier verifier,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<ProfileService> logger) : IProfileService
{
    public Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "UpdateProfile", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<RequestEmailCodeResponse>>(logger, "RequestEmailCode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Una espera o un tope de pedidos, o un correo inválido, no dejan nada; el correo se encola adentro, antes
            // del commit.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => RequestEmailCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "ConfirmEmail", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => ConfirmEmailCoreAsync(request, ct),
                // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el correo sea de otra
                // cuenta.
                CommitPolicy.OnAnyResult,
                cancellationToken);
        });
    }

    private async Task<Result> UpdateCoreAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return UserErrors.NotFound;
        }

        // UserManager autoguarda dentro de la transacción: si algo falla después, no queda nada.
        await userRepository.UpdateProfileAsync(
            userId, request.DisplayName, request.Culture!, request.TimeZoneId!, cancellationToken);
        return Result.Success();
    }

    // El pedido de código, ya validado: corre dentro del límite, con OnSuccess.
    private async Task<Result<RequestEmailCodeResponse>> RequestEmailCodeCoreAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        return await issuer.RequestEmailCodeAsync(user, request, cancellationToken);
    }

    // La confirmación, ya validada: corre dentro del límite con OnAnyResult, porque la verificación puede contar un
    // intento o gastar el código aunque la cuenta no exista, el correo esté ocupado o la escritura choque con el índice
    // único.
    private async Task<Result> ConfirmEmailCoreAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var verification = await verifier.VerifyAsync(
            LoginCodeDestination.ForEmail(email), user.Id, request.Code!, cancellationToken);
        if (verification.IsFailure)
        {
            return verification.Error;
        }

        // El índice único conserva también los correos de cuentas borradas.
        var owner = await users.FindByEmailAsync(email, cancellationToken);
        if (owner is null ? await users.ExistsDeletedByEmailAsync(email, cancellationToken) : owner.Id != user.Id)
        {
            return UserErrors.AlreadyExists;
        }

        try
        {
            await userRepository.SetEmailAsync(user.Id, email, confirmed: true, cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            // El savepoint deshizo solo ese guardado: el código sigue gastado y se confirma igual.
            return UserErrors.AlreadyExists;
        }

        return Result.Success();
    }
}
