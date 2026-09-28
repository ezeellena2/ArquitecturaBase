using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

internal sealed class ProfileService(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits,
    IPhoneNumberParser phoneNumbers,
    IRequestValidator validator,
    ProfileEmailOperations emailOperations,
    ProfileWhatsAppOperations whatsAppOperations,
    IUnitOfWork unitOfWork,
    ILogger<ProfileService> logger) : IProfileService
{
    public Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<CurrentUserResponse>>(logger, "GetProfile", async () =>
        {
            var user = currentUser.UserId is { } userId
                ? await users.FindByIdAsync(userId, cancellationToken)
                : null;

            if (user is null)
            {
                return UserErrors.NotFound;
            }

            var roles = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);
            var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

            // Sin número, Create falla y los dos quedan en null, igual que el número.
            var phone = PhoneNumber.Create(user.PhoneNumber);

            return new CurrentUserResponse(
                user.Id,
                user.Email,
                user.EmailConfirmed,
                user.PhoneNumber,
                phone.IsSuccess ? phoneNumbers.FormatInternational(phone.Value) : null,
                phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null,
                user.PhoneNumberConfirmed,
                await users.ExistsExternalLoginAsync(user.Id, ExternalLoginProviders.Google, cancellationToken),
                user.DisplayName,
                user.Culture,
                user.TimeZoneId,
                [.. roles.Order(StringComparer.Ordinal)],
                [.. permissions.Order(StringComparer.Ordinal)],
                await loginAudits.FindLastSuccessAtUtcAsync(user.Id, cancellationToken));
        });

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
            if (await emailOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Una espera o un tope de pedidos, o un correo inválido, no dejan nada; el correo se encola adentro, antes
            // del commit.
            return await unitOfWork.ExecuteInTransactionAsync(
                ct => emailOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "ConfirmEmail", async () =>
        {
            if (await emailOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => emailOperations.ConfirmAsync(request, ct),
                // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el correo sea de otra
                // cuenta.
                CommitPolicy.OnAnyResult,
                cancellationToken);
        });
    }

    public Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result<RequestPhoneLinkCodeResponse>>(logger, "RequestPhoneLinkCode", async () =>
        {
            if (await whatsAppOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            // Afuera y antes del límite, como en AccountService: con WhatsApp apagado es un error de programación, y un
            // error de configuración no abre transacción.
            whatsAppOperations.EnsureEnabled();

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => whatsAppOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });
    }

    public Task<Result> ConfirmPhoneLinkAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return OperationLog.RunAsync<Result>(logger, "ConfirmPhoneLink", async () =>
        {
            if (await whatsAppOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
            {
                return validationError;
            }

            return await unitOfWork.ExecuteInTransactionAsync(
                ct => whatsAppOperations.ConfirmAsync(request, ct),
                // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el número sea de otra
                // cuenta o la cuenta ya no exista.
                CommitPolicy.OnAnyResult,
                cancellationToken);
        });
    }

    public Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "UnlinkOwnPhone", () =>
            // Sin validador: todo va adentro. A propósito no revoca sesiones (solo un administrador las corta).
            unitOfWork.ExecuteInTransactionAsync(
                ct => whatsAppOperations.UnlinkAsync(ct), CommitPolicy.OnSuccess, cancellationToken));

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
}
