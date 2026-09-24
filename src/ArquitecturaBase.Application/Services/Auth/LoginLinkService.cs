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
    ISecureTokenGenerator tokens,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    TimeProvider timeProvider,
    ServiceRequestValidator<PreviewLoginLinkRequest> validator,
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

    // El nombre del caso de uso en los logs permanece estable para el diagnóstico y las pruebas de privacidad.
    [LoggerMessage(Level = LogLevel.Information, Message = "Handling PreviewLoginLinkQuery")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled PreviewLoginLinkQuery")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "PreviewLoginLinkQuery failed with {ErrorCode}")]
    private static partial void LogFailed(ILogger logger, string errorCode);
}
