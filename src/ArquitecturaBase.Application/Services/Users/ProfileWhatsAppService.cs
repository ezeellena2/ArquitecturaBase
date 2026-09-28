using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// El WhatsApp propio desde el perfil: pedir el código, confirmar el número y desvincularlo; conserva locks, cuotas y
/// consumo de códigos. El código lo emite <see cref="DestinationCodeIssuer"/>, y los pasos que comparte con la
/// administración (el orden de los locks y la anulación de enlaces) los da <see cref="PhoneNumberLinker"/>.
/// Desvincular el número propio es un flujo aparte del de un administrador: mira que quede otro medio de ingreso, no
/// revoca las sesiones y siempre invalida los enlaces.
/// </summary>
internal sealed class ProfileWhatsAppService(
    ICurrentUser currentUser,
    IUserReader users,
    UserGuards guards,
    DestinationCodeIssuer issuer,
    PhoneNumberLinker phoneLinker,
    IRequestValidator validator,
    IUnitOfWork unitOfWork,
    ILogger<ProfileWhatsAppService> logger) : IProfileWhatsAppService
{
    public Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<RequestPhoneLinkCodeResponse>>(logger, "RequestPhoneLinkCode", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Afuera y antes del límite, como en LoginCodeService: con WhatsApp apagado es un error de programación, y un
            // error de configuración no abre transacción.
            issuer.EnsureWhatsAppEnabled();

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => RequestPhoneLinkCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> ConfirmPhoneLinkAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "ConfirmPhoneLink", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => ConfirmPhoneLinkCoreAsync(request, ct),
                // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el número sea de otra
                // cuenta o la cuenta ya no exista.
                CommitPolicy.OnAnyResult,
                cancellationToken);
        });
    }

    public Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "UnlinkOwnPhone", () =>
            // Sin validador: todo va adentro. A propósito no revoca sesiones (solo un administrador las corta).
            unitOfWork.ExecuteInTransactionAsync(UnlinkOwnPhoneCoreAsync, CommitPolicy.OnSuccess, cancellationToken));

    // El pedido de código, ya validado y con WhatsApp prendido: corre dentro del límite, con OnSuccess.
    private async Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeCoreAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        return await issuer.RequestPhoneCodeAsync(user, request, cancellationToken);
    }

    // La confirmación, ya validada: corre dentro del límite, con OnAnyResult.
    private async Task<Result> ConfirmPhoneLinkCoreAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = PhoneNumber.Create(request.Phone);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        return await phoneLinker.ConfirmOwnPhoneAsync(userId, phoneResult.Value, request.Code!, cancellationToken);
    }

    // Desvincular el número propio: corre dentro del límite, con OnSuccess.
    private async Task<Result> UnlinkOwnPhoneCoreAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return UserErrors.NotFound;
        }

        await phoneLinker.LockAsync(userId, newPhone: null, cancellationToken);
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // La persona conserva la sesión actual; solo un administrador revoca sesiones al quitar un número.
        if (user.PhoneNumber is not null)
        {
            if (!await guards.HasOtherLoginMethodAsync(user, cancellationToken))
            {
                return UserErrors.LastLoginMethod;
            }

            await phoneLinker.RemovePhoneAsync(user.Id, cancellationToken);
        }

        await phoneLinker.ReleaseContactAsync(user.Id, cancellationToken);
        await phoneLinker.VoidPendingLinksAsync(user.Id, cancellationToken);
        return Result.Success();
    }
}
