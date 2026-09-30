using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class LoginMethodsService(
    IGoogleAvailability google,
    IPhoneChannel phone,
    ISystemSettingsReader settings,
    ILogger<LoginMethodsService> logger) : ILoginMethodsService
{
    public Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<LoginMethodsResponse>>(logger, "GetLoginMethods", async () =>
        {
            var registrationOpen = await settings.FindRegistrationModeAsync(cancellationToken) == RegistrationMode.Open;
            LoginMethodsResponse response = phone.IsEnabled
                ? new(google.IsEnabled, WhatsApp: true, phone.Countries, phone.DisplayNumber, registrationOpen)
                : new(google.IsEnabled, WhatsApp: false, WhatsAppCountries: [], WhatsAppNumber: null, registrationOpen);

            return response;
        });
}
