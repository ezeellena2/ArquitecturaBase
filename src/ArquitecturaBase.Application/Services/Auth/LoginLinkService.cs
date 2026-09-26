using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

public sealed partial class LoginLinkService(
    ILoginLinkRepository loginLinks,
    ILoginAuditRepository loginAudits,
    ISecureTokenGenerator tokens,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    IRequestInfo requestInfo,
    TimeProvider timeProvider,
    ServiceRequestValidator<PreviewLoginLinkRequest> validator,
    ServiceRequestValidator<RedeemLoginLinkRequest> redeemValidator,
    IUnitOfWork unitOfWork,
    ILogger<LoginLinkService> logger) : ILoginLinkService
{
    public async Task<Result<LoginLinkPreviewResponse>> PreviewAsync(
        PreviewLoginLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogHandling(logger);

        var validationError = await validator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogFailed(logger, validationError.Code);
            return validationError;
        }

        var loginLink = await loginLinks.GetByTokenHashAsync(tokens.Hash(request.Token!), cancellationToken);
        if (loginLink is null || !loginLink.IsActive(timeProvider.GetUtcNow().UtcDateTime))
        {
            LogFailed(logger, LoginLinkErrors.InvalidCode);
            return LoginLinkErrors.Invalid;
        }

        // Una cuenta borrada después de emitir el enlace no puede revelar sus datos en la vista previa.
        var user = await identityService.FindByIdAsync(loginLink.UserId, cancellationToken);
        if (user is null)
        {
            LogFailed(logger, LoginLinkErrors.InvalidCode);
            return LoginLinkErrors.Invalid;
        }

        LogHandled(logger);
        return new LoginLinkPreviewResponse(user.DisplayName ?? user.Email, MaskedPhoneOf(user));
    }

    private string? MaskedPhoneOf(UserAccount user)
    {
        var phone = PhoneNumber.Create(user.PhoneNumber);
        return phone.IsSuccess ? phoneNumbers.Mask(phone.Value) : null;
    }

    public async Task<Result> RedeemAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRedeemHandling(logger);

        var validationError = await redeemValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogRedeemFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await RedeemCoreAsync(request, cancellationToken);

        // Se guarda con cualquier resultado del canje: si la cuenta está bloqueada o deshabilitada, el enlace igual
        // queda consumido, y todo intento sobre una cuenta existente deja su auditoría.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result.IsSuccess)
        {
            LogRedeemHandled(logger);
        }
        else
        {
            LogRedeemFailed(logger, result.Error.Code);
        }

        return result;
    }

    private async Task<Result> RedeemCoreAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
    {
        var tokenHash = tokens.Hash(request.Token!);

        // Un enlace inventado no pertenece a ninguna cuenta y no genera fila de auditoría.
        var userId = await loginLinks.FindUserIdAsync(tokenHash, cancellationToken);
        if (userId is null)
        {
            return LoginLinkErrors.Invalid;
        }

        // Se toma el lock por cuenta y luego se vuelve a leer: solo un canje puede consumir el enlace.
        await loginLinks.LockAccountAsync(userId.Value, cancellationToken);

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var loginLink = await loginLinks.GetByTokenHashAsync(tokenHash, cancellationToken);
        var redemption = loginLink?.Redeem(nowUtc) ?? Result.Failure(LoginLinkErrors.Invalid);

        var user = await identityService.FindByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        if (redemption.IsFailure)
        {
            return Fail(user, redemption.Error, nowUtc);
        }

        if (await identityService.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user, AccountErrors.LockedOut, nowUtc);
        }

        if (!user.IsActive)
        {
            return Fail(user, AccountErrors.Disabled, nowUtc);
        }

        await identityService.ResetFailedAttemptsAsync(user.Id, cancellationToken);
        await identityService.SignInAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return Result.Success();
    }

    private static string IdentifierOf(UserAccount user) =>
        user.PhoneNumber ?? user.Email ?? throw new InvalidOperationException("Every account has an email or a phone number.");

    private Error Fail(UserAccount user, Error error, DateTime nowUtc)
    {
        loginAudits.Add(LoginAudit.Failure(
            IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, error.Code, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));
        return error;
    }

    // Solo la operación y el código de error: el token y la URL nunca van al log. La prueba de privacidad de los
    // enlaces busca estas líneas para confirmar que el log se capturó.
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling PreviewLoginLink")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled PreviewLoginLink")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PreviewLoginLink failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling RedeemLoginLink")]
    private static partial void LogRedeemHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled RedeemLoginLink")]
    private static partial void LogRedeemHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RedeemLoginLink failed with {ErrorCode}")]
    private static partial void LogRedeemFailed(ILogger logger, string errorCode);
}
