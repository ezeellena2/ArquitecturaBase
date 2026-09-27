using ArquitecturaBase.Application.Configuration.Auth;
using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
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
    IUserReader users,
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

        if (await requestLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRequestLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        // Un correo inválido, una espera o un tope de pedidos no dejan nada. El correo se encola adentro, antes del
        // commit, para que la fila se confirme ya marcada como enviada; si el commit falla, el correo sale igual, con
        // un código que no sirve.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RequestLoginCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            LogRequestLoginCodeHandled(logger);
        }
        else
        {
            LogRequestLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

    public async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeAsync(
        RequestWhatsAppLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogRequestWhatsAppLoginCodeHandling(logger);

        if (await requestWhatsAppLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogRequestWhatsAppLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        // La ruta HTTP se omite cuando WhatsApp está apagado. Llegar hasta aquí es un error de programación.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }

        // Que la cola no tome el mensaje no es un fallo: el código se confirma sin fecha de envío.
        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => RequestWhatsAppLoginCodeCoreAsync(request, ct), CommitPolicy.OnSuccess, cancellationToken);

        if (result.IsSuccess)
        {
            LogRequestWhatsAppLoginCodeHandled(logger);
        }
        else
        {
            LogRequestWhatsAppLoginCodeFailed(logger, result.Error.Code);
        }

        return result;
    }

    public async Task<Result<VerifyLoginCodeResponse>> VerifyLoginCodeAsync(
        VerifyLoginCodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        LogVerifyLoginCodeHandling(logger);

        if (await verifyLoginCodeValidator.ValidateAsync(request, cancellationToken) is { } validationError)
        {
            LogVerifyLoginCodeFailed(logger, validationError.Code);
            return validationError;
        }

        var result = await unitOfWork.ExecuteInTransactionAsync(
            ct => verifier.VerifyAsync(request, ct),
            // Un código equivocado cuenta el intento, un bloqueo deja su auditoría y un NotInvited o un Disabled gastan el
            // código: el error también se confirma. Una excepción igual deshace todo.
            CommitPolicy.OnAnyResult,
            cancellationToken);

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

    // El pedido por correo, ya validado: corre dentro del límite de RequestLoginCodeAsync.
    private async Task<Result<RequestLoginCodeResponse>> RequestLoginCodeCoreAsync(
        RequestLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForEmail(email), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var settings = loginCodeOptions.Value;
        var user = await users.FindByEmailAsync(email, cancellationToken);

        // La fila también se guarda para un correo desconocido en InviteOnly: los límites no pueden revelar
        // si existe la cuenta. El administrador inicial puede recibir el email antes de crear su cuenta.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email, cancellationToken))
        {
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            // Se encola antes del commit para que la fila se confirme ya marcada como enviada. Encolar no espera al
            // SMTP, así que no alarga el lock del destino.
            await emailQueue.EnqueueAsync(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, settings.LifetimeMinutes, culture),
                cancellationToken);
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    // El pedido por WhatsApp, ya validado y con WhatsApp prendido: corre dentro del límite de RequestWhatsAppLoginCodeAsync.
    private async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeCoreAsync(
        RequestWhatsAppLoginCodeRequest request, CancellationToken cancellationToken)
    {
        var phoneResult = phoneNumbers.Parse(request.Country, request.Number);
        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await users.FindByPhoneAsync(phone, cancellationToken);

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

        return new RequestWhatsAppLoginCodeResponse(
            loginCodeOptions.Value.ResendCooldownSeconds,
            phone.Value,
            phoneNumbers.Mask(phone));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling GetLoginMethods")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled GetLoginMethods")]
    private static partial void LogHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling RequestLoginCode")]
    private static partial void LogRequestLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled RequestLoginCode")]
    private static partial void LogRequestLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RequestLoginCode failed with {ErrorCode}")]
    private static partial void LogRequestLoginCodeFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling RequestWhatsAppLoginCode")]
    private static partial void LogRequestWhatsAppLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled RequestWhatsAppLoginCode")]
    private static partial void LogRequestWhatsAppLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "RequestWhatsAppLoginCode failed with {ErrorCode}")]
    private static partial void LogRequestWhatsAppLoginCodeFailed(ILogger logger, string errorCode);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling VerifyLoginCode")]
    private static partial void LogVerifyLoginCodeHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled VerifyLoginCode")]
    private static partial void LogVerifyLoginCodeHandled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "VerifyLoginCode failed with {ErrorCode}")]
    private static partial void LogVerifyLoginCodeFailed(ILogger logger, string errorCode);
}
