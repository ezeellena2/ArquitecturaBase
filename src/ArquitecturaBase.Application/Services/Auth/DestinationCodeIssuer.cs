using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// Los dos pedidos de un código con que una cuenta prueba, desde el perfil, que un correo o un número es suyo (sección 12
/// del spec del ingreso con WhatsApp): emite el código con <see cref="LoginCodeIssuer"/>, que sigue siendo el único que
/// emite, y se lo manda a ese destino. Lo verifica <see cref="DestinationCodeVerifier"/>. Corre dentro del límite del
/// punto de entrada, con OnSuccess: no guarda, y lo que encola queda marcado en la fila antes del commit.
/// </summary>
internal sealed class DestinationCodeIssuer(
    LoginCodeIssuer issuer,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppAvailability whatsApp,
    IWhatsAppSendQueue sendQueue,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    IOptions<WhatsAppLoginOptions> whatsAppOptions)
{
    /// <summary>El pedido por correo de <paramref name="user"/>, ya validado.</summary>
    public async Task<Result<RequestEmailCodeResponse>> RequestEmailCodeAsync(
        UserAccount user, RequestEmailCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

        var emailResult = Email.Create(request.Email);
        if (emailResult.IsFailure)
        {
            return emailResult.Error;
        }

        var email = emailResult.Value;
        var issued = await issuer.IssueVerificationCodeAsync(LoginCodeDestination.ForEmail(email), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Se encola antes del commit para que el código se confirme ya marcado como enviado. Si la cola está llena, el
        // código se guarda como no enviado.
        if (emailQueue.TryEnqueue(
            templateRenderer.RenderEmailVerificationCode(
                email.Value, issued.Value.Code, issued.Value.LifetimeMinutes, CultureInfo.GetCultureInfo(UserCultures.Of(user)))))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestEmailCodeResponse(issued.Value.ResendCooldownSeconds);
    }

    /// <summary>
    /// La acción no existe con WhatsApp apagado. Este guard impide emitir un código sin entrega si se invoca directamente
    /// el servicio desde otro consumidor. El punto de entrada lo llama después de validar y antes de abrir el límite, como
    /// LoginCodeService: un error de configuración no abre transacción.
    /// </summary>
    public void EnsureWhatsAppEnabled()
    {
        if (!whatsApp.IsEnabled)
        {
            throw new InvalidOperationException("WhatsApp is disabled (no WhatsApp:PhoneNumberId): no code to link a number can be requested.");
        }
    }

    /// <summary>El pedido por WhatsApp de <paramref name="user"/>, ya validado y con WhatsApp prendido.</summary>
    public async Task<Result<RequestPhoneLinkCodeResponse>> RequestPhoneCodeAsync(
        UserAccount user, RequestPhoneLinkCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);

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

        // El emisor comparte límites por destino y cuota diaria con los códigos de ingreso.
        var issued = await issuer.IssueVerificationCodeAsync(
            LoginCodeDestination.ForPhone(phone), user.Id, cancellationToken);
        if (issued.IsFailure)
        {
            return issued.Error;
        }

        // Si la cola no lo toma, queda sin fecha de envío y no consume la cuota. Se marca antes del commit.
        if (sendQueue.TryEnqueue(new WhatsAppLoginCodeMessage(phone, UserCultures.Of(user), issued.Value.Code)))
        {
            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestPhoneLinkCodeResponse(issued.Value.ResendCooldownSeconds, phone.Value, phoneNumbers.Mask(phone));
    }
}
