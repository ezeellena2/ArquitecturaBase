using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.GetLoginMethods;

/// <summary>
/// Lo que la pantalla de login tiene que ofrecer, según la configuración: sin su ClientId, Google no aparece; sin su
/// PhoneNumberId, WhatsApp tampoco (sección 10 del spec del ingreso con WhatsApp).
/// </summary>
internal sealed class GetLoginMethodsQueryHandler(
    IGoogleAvailability google,
    IWhatsAppAvailability whatsApp,
    IOptions<WhatsAppLoginOptions> whatsAppOptions)
    : IQueryHandler<GetLoginMethodsQuery, LoginMethodsResponse>
{
    public Task<Result<LoginMethodsResponse>> Handle(GetLoginMethodsQuery query, CancellationToken cancellationToken)
    {
        var settings = whatsAppOptions.Value;

        var response = whatsApp.IsEnabled
            ? new LoginMethodsResponse(google.IsEnabled, WhatsApp: true, settings.Countries, settings.DisplayPhoneNumber)
            : new LoginMethodsResponse(google.IsEnabled, WhatsApp: false, WhatsAppCountries: [], WhatsAppNumber: null);

        return Task.FromResult<Result<LoginMethodsResponse>>(response);
    }
}
