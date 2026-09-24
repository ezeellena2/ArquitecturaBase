using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.WhatsApp;
using ArquitecturaBase.Application.Features.Auth;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Users.RequestPhoneLinkCode;

/// <summary>
/// Pide el código para vincular un número de WhatsApp a la propia cuenta (sección 12 del spec del ingreso con
/// WhatsApp): la misma plantilla de autenticación que el ingreso, en el idioma de la cuenta, con un código que solo
/// sirve para vincular y solo para esa cuenta. La respuesta, el código y el envío son los mismos sea el número libre o
/// de otra cuenta, activa o borrada: decir antes que el número ya tiene dueño le contaría a cualquiera qué números
/// están registrados. Eso se dice al confirmar, recién con el código correcto, que solo tiene quien es dueño del número.
/// Valen los mismos controles que al entrar: los países permitidos, el tope diario y los límites por número.
/// </summary>
internal sealed class RequestPhoneLinkCodeCommandHandler(
    ICurrentUser currentUser,
    IIdentityService identityService,
    LoginCodeIssuer issuer,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppOutbox outbox,
    IOptions<WhatsAppLoginOptions> whatsAppOptions,
    IOptions<LoginCodeOptions> options)
    : ICommandHandler<RequestPhoneLinkCodeCommand, RequestPhoneLinkCodeResponse>
{
    public async Task<Result<RequestPhoneLinkCodeResponse>> Handle(
        RequestPhoneLinkCodeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // Con WhatsApp apagado el endpoint ni se mapea, como el del ingreso. Llegar acá sería emitir un código que nunca
        // sale: es un error de programación, no algo que decida quien pide.
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no code to link a number can be requested.");
        }

        var user = currentUser.UserId is { } userId
            ? await identityService.FindByIdAsync(userId, cancellationToken)
            : null;

        if (user is null)
        {
            return UserErrors.NotFound;
        }

        var phoneResult = phoneNumbers.Parse(command.Country, command.Number);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error;
        }

        var phone = phoneResult.Value;

        if (!whatsAppOptions.Value.AllowsCountry(phoneNumbers.RegionOf(phone)))
        {
            return WhatsAppErrors.CountryNotSupported;
        }

        var issued = await issuer.IssueVerificationCodeAsync(LoginCodeDestination.ForPhone(phone), user.Id, cancellationToken);

        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Si la cola no lo toma, el código queda sin fecha de envío: no cuenta para el tope y la persona pide otro.
        if (outbox.TryEnqueue(new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code)))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestPhoneLinkCodeResponse(options.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }
}
