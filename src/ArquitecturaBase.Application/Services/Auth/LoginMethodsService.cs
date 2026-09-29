using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class LoginMethodsService(
    IGoogleAvailability google,
    IWhatsAppAvailability whatsApp,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    ILogger<LoginMethodsService> logger) : ILoginMethodsService
{
    public Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<LoginMethodsResponse>>(logger, "GetLoginMethods", () =>
        {
            var settings = whatsAppOptions.Value;

            LoginMethodsResponse response = whatsApp.IsEnabled
                ? new(google.IsEnabled, WhatsApp: true, settings.Countries, settings.DisplayPhoneNumber)
                : new(google.IsEnabled, WhatsApp: false, WhatsAppCountries: [], WhatsAppNumber: null);

            return Task.FromResult<Result<LoginMethodsResponse>>(response);
        });
}
