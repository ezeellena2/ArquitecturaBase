using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Users;

internal sealed partial class ProfileService(
    ICurrentUser currentUser,
    IUserReader users,
    IUserRepository userRepository,
    IPermissionService permissionService,
    ILoginAuditRepository loginAudits,
    IPhoneNumberParser phoneNumbers,
    ServiceRequestValidator<UpdateProfileRequest> updateValidator,
    ProfileEmailOperations emailOperations,
    ProfileWhatsAppOperations whatsAppOperations,
    IUnitOfWork unitOfWork,
    ILogger<ProfileService> logger) : IProfileService
{
    private const string RequestName = "GetProfile";
    private const string UpdateRequestName = "UpdateProfile";
    private const string RequestEmailCodeName = "RequestEmailCode";
    private const string ConfirmEmailName = "ConfirmEmail";
    private const string RequestPhoneLinkCodeName = "RequestPhoneLinkCode";
    private const string ConfirmPhoneLinkName = "ConfirmPhoneLink";
    private const string UnlinkOwnPhoneName = "UnlinkOwnPhone";

    public async Task<Result<CurrentUserResponse>> GetAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, RequestName);

        var user = currentUser.UserId is { } userId
            ? await users.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            LogFailed(logger, RequestName, UserErrors.NotFoundCode);
            return UserErrors.NotFound;
        }

        var roles = await users.ListRoleNamesForUserAsync(user.Id, cancellationToken);
        var permissions = await permissionService.GetPermissionsAsync(user.Id, cancellationToken);

        // Sin número, Create falla y los dos quedan en null, igual que el número.
        var phone = PhoneNumber.Create(user.PhoneNumber);

        var response = new CurrentUserResponse(
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
            await loginAudits.GetLastSuccessAtUtcAsync(user.Id, cancellationToken));

        LogHandled(logger, RequestName);
        return response;
    }

    public async Task<Result> UpdateAsync(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, UpdateRequestName);

        if (await updateValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(UpdateRequestName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => UpdateCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(UpdateRequestName, result);
        return result;
    }

    public async Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, RequestEmailCodeName);

        if (await emailOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(RequestEmailCodeName, validationError);
            return validationError;
        }

        // Una espera o un tope de pedidos, o un correo inválido, no dejan nada; el correo se encola adentro, antes del
        // commit.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => emailOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(RequestEmailCodeName, result);
        return result;
    }

    public async Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, ConfirmEmailName);

        if (await emailOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(ConfirmEmailName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => emailOperations.ConfirmAsync(request, ct),
            // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el correo sea de otra cuenta.
            CommitPolicy.OnAnyResult,
            cancellationToken);
        LogOutcome(ConfirmEmailName, result);
        return result;
    }

    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneLinkCodeAsync(
        RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, RequestPhoneLinkCodeName);

        if (await whatsAppOperations.ValidateRequestAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(RequestPhoneLinkCodeName, validationError);
            return validationError;
        }

        // Afuera y antes del límite, como en AccountService: con WhatsApp apagado es un error de programación, y un error
        // de configuración no abre transacción.
        whatsAppOperations.EnsureEnabled();

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.RequestCodeAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(RequestPhoneLinkCodeName, result);
        return result;
    }

    public async Task<Result> ConfirmPhoneLinkAsync(ConfirmPhoneLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger, ConfirmPhoneLinkName);

        if (await whatsAppOperations.ValidateConfirmAsync(request, cancellationToken) is { } validationError)
        {
            LogOutcome(ConfirmPhoneLinkName, validationError);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.ConfirmAsync(request, ct),
            // Un código equivocado cuenta el intento, y uno correcto queda gastado aunque el número sea de otra cuenta o
            // la cuenta ya no exista.
            CommitPolicy.OnAnyResult,
            cancellationToken);
        LogOutcome(ConfirmPhoneLinkName, result);
        return result;
    }

    public async Task<Result> UnlinkOwnPhoneAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger, UnlinkOwnPhoneName);
        // Sin validador: todo va adentro. A propósito no revoca sesiones (solo un administrador las corta).
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => whatsAppOperations.UnlinkAsync(ct), CommitPolicy.OnSuccess, cancellationToken);
        LogOutcome(UnlinkOwnPhoneName, result);
        return result;
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

    private void LogOutcome(string requestName, Result result)
    {
        if (result.IsSuccess)
        {
            LogHandled(logger, requestName);
        }
        else
        {
            LogFailed(logger, requestName, result.Error.Code);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling {RequestName}")]
    private static partial void LogHandling(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled {RequestName}")]
    private static partial void LogHandled(ILogger logger, string requestName);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{RequestName} failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string requestName, string errorCode);
}
