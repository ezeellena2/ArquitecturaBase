using ArquitecturaBase.Application.Common.Logging;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class LoginMethodsService(
    IGoogleAvailability google,
    IPhoneChannel phone,
    ILogger<LoginMethodsService> logger) : ILoginMethodsService
{
    public Task<Result<LoginMethodsResponse>> GetLoginMethodsAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<LoginMethodsResponse>>(logger, "GetLoginMethods", () =>
        {
            LoginMethodsResponse response = phone.IsEnabled
                ? new(google.IsEnabled, WhatsApp: true, phone.Countries, phone.DisplayNumber)
                : new(google.IsEnabled, WhatsApp: false, WhatsAppCountries: [], WhatsAppNumber: null);

            return Task.FromResult<Result<LoginMethodsResponse>>(response);
        });
}
