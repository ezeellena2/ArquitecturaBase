using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Models.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed partial class AccountService(
    IGoogleAvailability google,
    IWhatsAppAvailability whatsApp,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    LoginCodeIssuer issuer,
    LoginCodeVerifier verifier,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppOutbox outbox,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    AccountCreationPolicy accountCreation,
    IOptions<LoginCodeOptions> loginCodeOptions,
    ServiceRequestValidator<RequestLoginCodeRequest> requestLoginCodeValidator,
    ServiceRequestValidator<RequestWhatsAppLoginCodeRequest> requestWhatsAppLoginCodeValidator,
    ServiceRequestValidator<VerifyLoginCodeRequest> verifyLoginCodeValidator,
    IUnitOfWork unitOfWork,
    ILogger<AccountService> logger) : IAccountService
{
    public Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken)
    {
        LogHandling(logger);
        var settings = whatsAppOptions.Value;

        LoginMethodsResponse response = whatsApp.IsEnabled
            ? new(google.IsEnabled, WhatsApp: true, settings.Countries, settings.DisplayPhoneNumber)
            : new(google.IsEnabled, WhatsApp: false, WhatsAppCountries: [], WhatsAppNumber: null);

        LogHandled(logger);
        return Task.FromResult<Result<LoginMethodsResponse>>(response);
    }

    public async Task<Result<RequestLoginCodeResponse>> RequestLoginCodeAsync(
        RequestLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequestLoginCodeHandling(logger);

        var validationError = await requestLoginCodeValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogRequestLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            LogRequestLoginCodeFailed(logger, emailResult.Error.Code);
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        if (issued.IsFailure)
        {
            LogRequestLoginCodeFailed(logger, issued.Error.Code);
            return issued.Error;
        }

        var settings = loginCodeOptions.Value;
        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        // La fila también se guarda para un correo desconocido en InviteOnly: los límites no pueden revelar
        // si existe la cuenta. El administrador inicial puede recibir el email antes de crear su cuenta.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email, cancellationToken))
        {
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            // La cola acepta el email antes de confirmar la fila, igual que el caso de uso heredado.
            await emailQueue.EnqueueAsync(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, settings.LifetimeMinutes, culture),
                cancellationToken);
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogRequestLoginCodeHandled(logger);
        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    public async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequestWhatsAppLoginCodeHandling(logger);

        var validationError = await requestWhatsAppLoginCodeValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogRequestWhatsAppLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        // La ruta HTTP se omite cuando WhatsApp está apagado. Llegar hasta aquí es un error de programación.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }

        var phoneResult = phoneNumbers.Parse(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            LogRequestWhatsAppLoginCodeFailed(logger, phoneResult.Error.Code);
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            LogRequestWhatsAppLoginCodeFailed(logger, WhatsAppErrors.CountryNotSupported.Code);
            return WhatsAppErrors.CountryNotSupported;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
        if (issued.IsFailure)
        {
            LogRequestWhatsAppLoginCodeFailed(logger, issued.Error.Code);
            return issued.Error;
        }

        var user = await identityService.FindByPhoneAsync(phone, cancellationToken);

        // También se guarda la fila de un número desconocido en InviteOnly: sostiene los mismos límites por destino.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken))
        {
            var message = new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code);

            // La cola puede rechazar el mensaje. En ese caso se guarda el código como no enviado.
            if (outbox.TryEnqueue(message))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogRequestWhatsAppLoginCodeHandled(logger);
        return new RequestWhatsAppLoginCodeResponse(
            loginCodeOptions.Value.ResendCooldownSeconds,
            phone.Value,
            phoneNumbers.Mask(phone));
    }

    public async Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogVerifyLoginCodeHandling(logger);

        var validationError = await verifyLoginCodeValidator.ValidateAsync(request, cancellationToken);
        if (validationError is not null)
        {
            LogVerifyLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await verifier.VerifyAsync(request, cancellationToken);

        // Como IPersistChangesOnFailure del comando heredado: la verificación puede consumir un código, contar un
        // intento o agregar una auditoría aunque devuelva error. El lock del destino termina al confirmar esta unidad.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (result.IsSuccess)
        {
            LogVerifyLoginCodeHandled(logger);
        }
        else
        {
            LogVerifyLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling GetLoginMethods")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled GetLoginMethods")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling RequestLoginCodeCommand")]
    private static partial void LogRequestLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled RequestLoginCodeCommand")]
    private static partial void LogRequestLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RequestLoginCodeCommand failed with {ErrorCode}")]
    private static partial void LogRequestLoginCodeFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling RequestWhatsAppLoginCodeCommand")]
    private static partial void LogRequestWhatsAppLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled RequestWhatsAppLoginCodeCommand")]
    private static partial void LogRequestWhatsAppLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RequestWhatsAppLoginCodeCommand failed with {ErrorCode}")]
    private static partial void LogRequestWhatsAppLoginCodeFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling VerifyLoginCodeCommand")]
    private static partial void LogVerifyLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled VerifyLoginCodeCommand")]
    private static partial void LogVerifyLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VerifyLoginCodeCommand failed with {ErrorCode}")]
    private static partial void LogVerifyLoginCodeFailed(ILogger logger, string errorCode);
}
