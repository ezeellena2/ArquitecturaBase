using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Integrations.Security;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed partial class LoginLinkService(
    ILoginLinkRepository loginLinks,
    ILoginAuditRepository loginAudits,
    ISecureTokenGenerator tokens,
    IUserReader users,
    ISignInService signIn,
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

        if (await validator.ValidateAsync(request, cancellationToken) is { } validationError)
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
        var user = await users.FindByIdAsync(loginLink.UserId, cancellationToken);
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

        if (await redeemValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRedeemFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RedeemCoreAsync(request, ct),
            // El enlace queda consumido aunque la cuenta esté bloqueada, deshabilitada o borrada, y todo intento sobre una
            // cuenta existente deja su auditoría: el error también se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

        if (result.IsFailure)
        {
            LogRedeemFailed(logger, result.Error.Code);
            return result.Error;
        }

        // La cookie de la aplicación sale recién después del commit del enlace gastado y la auditoría, como en los otros
        // dos ingresos: SignInAsync lanza adentro de un límite.
        await signIn.SignInAsync(result.Value, cancellationToken);
        LogRedeemHandled(logger);

        return Result.Success();
    }

    private async Task<Result<Guid>> RedeemCoreAsync(RedeemLoginLinkRequest request, CancellationToken cancellationToken)
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

        var user = await users.FindByIdAsync(userId.Value, cancellationToken);
        if (user is null)
        {
            return LoginLinkErrors.Invalid;
        }

        if (redemption.IsFailure)
        {
            return Fail(user, redemption.Error, nowUtc);
        }

        if (await signIn.IsLockedOutAsync(user.Id, cancellationToken))
        {
            return Fail(user, AccountErrors.LockedOut, nowUtc);
        }

        if (!user.IsActive)
        {
            return Fail(user, AccountErrors.Disabled, nowUtc);
        }

        await signIn.ResetFailedAttemptsAsync(user.Id, cancellationToken);

        loginAudits.Add(LoginAudit.Success(
            IdentifierOf(user), user.Id, LoginMethod.WhatsAppLink, requestInfo.IpAddress, requestInfo.UserAgent, nowUtc));

        return user.Id;
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
