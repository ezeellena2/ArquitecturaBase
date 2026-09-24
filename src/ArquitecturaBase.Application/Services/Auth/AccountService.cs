using System.Globalization;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
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
    IIdentityService identityService,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    AccountCreationPolicy accountCreation,
    IOptions<LoginCodeOptions> loginCodeOptions,
    ServiceRequestValidator<RequestLoginCodeRequest> requestLoginCodeValidator,
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
}
