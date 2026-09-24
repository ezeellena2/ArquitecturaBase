using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

public sealed partial class AccountService(
    IGoogleAvailability google,
    IWhatsAppAvailability whatsApp,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Handling GetLoginMethods")]
    private static partial void LogHandling(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Handled GetLoginMethods")]
    private static partial void LogHandled(ILogger logger);
}
