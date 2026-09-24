using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestWhatsAppLoginCode;

/// <summary>
/// Emite un código para entrar con WhatsApp y encola la plantilla de autenticación (sección 10 del spec del ingreso
/// con WhatsApp). Las reglas son las del correo: el lock, el reenvío y el límite por número los aplica
/// <see cref="LoginCodeIssuer"/>, y en InviteOnly un número sin cuenta recorre el mismo camino sin que se le mande
/// nada. Suma dos controles propios, porque cada mensaje se paga: el país del número y un tope diario, que también
/// aplica el emisor porque vale para todo código por WhatsApp.
/// </summary>
internal sealed class RequestWhatsAppLoginCodeCommandHandler(
    LoginCodeIssuer issuer,
    IIdentityService identityService,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppOutbox outbox,
    AccountCreationPolicy accountCreation,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    IOptions<LoginCodeOptions> options)
    : ICommandHandler<RequestWhatsAppLoginCodeCommand, RequestWhatsAppLoginCodeResponse>
{
    public async Task<Result<RequestWhatsAppLoginCodeResponse>> Handle(
        RequestWhatsAppLoginCodeCommand command,
        CancellationToken cancellationToken)
    {
        // Con WhatsApp apagado el endpoint ni se mapea, así que acá no se llega. Si se llegara, se emitiría un código
        // que nunca sale: es un error de programación, no algo que decida quien pide.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no WhatsApp sign-in code can be requested.");
        }

        var phoneResult = phoneNumbers.Parse(command.Country, command.Number);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;

        // El país es el del número y no el elegido: "+598…" con Argentina elegida sigue siendo un número de Uruguay.
        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        var issued = await issuer.IssueSignInCodeAsync(LoginCodeDestination.ForPhone(phone), cancellationToken);

        if (issued.IsFailure)
        {
            return issued.Error;
        }

        var user = await identityService.FindByPhoneAsync(phone, cancellationToken);

        // Como con el correo: en InviteOnly, a un número sin cuenta (o de una cuenta borrada) se le emitió el código
        // pero no se le manda nada, y la respuesta es la misma. La fila se guarda a propósito: los límites por número
        // se apoyan en ella, y sin ella insistir con un número desconocido respondería 202 para siempre mientras uno
        // registrado empieza a responder 429, que alcanza para averiguar qué números tienen cuenta. Sin correo, la
        // excepción del administrador inicial no aplica: el número solo recibe el código si ya tiene cuenta o en Open.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email: null, cancellationToken))
        {
            var message = new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code);

            // Si la cola no lo toma, el código queda sin fecha de envío: no cuenta para el tope y la persona pide otro.
            if (outbox.TryEnqueue(message))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestWhatsAppLoginCodeResponse(options.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }
}
