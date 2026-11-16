using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// El WhatsApp propio desde el perfil: pedir el código y confirmar el número, y pedir al número verificado el código que
/// confirma agregar un correo (cada medio confirma los cambios del otro, 2026-11-16); conserva locks, cuotas y consumo
/// de códigos. El código lo pide <see cref="WhatsAppCodeIssuer"/>, y los pasos que comparte con la
/// administración (el orden de los locks y la anulación de enlaces) los da <see cref="PhoneNumberLinker"/>.
/// </summary>
internal sealed class ProfileWhatsAppService(
    ICurrentUser currentUser,
    IUserReader users,
    WhatsAppCodeIssuer issuer,
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

            // Afuera y antes del límite, como en WhatsAppLoginCodeService: con WhatsApp apagado es un error de
            // programación, y un error de configuración no abre transacción.
            issuer.EnsureEnabledForLink();

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

    public Task<Result<RequestPhoneConfirmationCodeResponse>> RequestPhoneConfirmationCodeAsync(
        CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<RequestPhoneConfirmationCodeResponse>>(logger, "RequestPhoneConfirmationCode", () =>
        {
            // Como el de vincular: con WhatsApp apagado es un error de programación, y no abre transacción.
            issuer.EnsureEnabledForLink();

            return unitOfWork.ExecuteInTransactionAsync(
                RequestPhoneConfirmationCodeCoreAsync, CommitPolicy.OnSuccess, cancellationToken);
        });

    // El pedido del código que confirma agregar un correo: corre dentro del límite, con OnSuccess. Lo que no admite se
    // rechaza antes de emitir, así no se paga un mensaje que no sirve.
    private async Task<Result<RequestPhoneConfirmationCodeResponse>> RequestPhoneConfirmationCodeCoreAsync(
        CancellationToken cancellationToken)
    {
        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;
        if (user is null)
        {
            return UserErrors.NotFound;
        }

        // Un correo verificado no se cambia desde el perfil: no hay correo que agregar.
        if (user.EmailConfirmed)
        {
            return UserErrors.EmailAlreadySet;
        }

        return user is { PhoneNumber: { } value, PhoneNumberConfirmed: true } && PhoneNumber.Create(value) is { IsSuccess: true } phone
            ? await issuer.RequestConfirmationCodeAsync(user, phone.Value, cancellationToken)
            : UserErrors.NoVerifiedPhone;
    }

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

        return await issuer.RequestVerificationCodeAsync(user, request, cancellationToken);
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

        return await phoneLinker.ConfirmOwnPhoneAsync(
            userId, phoneResult.Value, request.Code!, request.EmailCode, cancellationToken);
    }
}
