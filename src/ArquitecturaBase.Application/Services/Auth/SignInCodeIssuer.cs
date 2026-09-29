using System.Globalization;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Auth;
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
/// Los dos pedidos de un código para entrar, ya validados: emite el código con <see cref="LoginCodeIssuer"/> y se lo
/// manda a la cuenta, por correo o por WhatsApp, o a quien todavía puede crear una. Corre dentro del límite de
/// <see cref="LoginCodeService"/>: no guarda, y lo que encola queda marcado en la fila antes del commit.
/// </summary>
internal sealed class SignInCodeIssuer(
    LoginCodeIssuer issuer,
    IUserReader users,
    IPhoneNumberParser phoneNumbers,
    IWhatsAppSendQueue sendQueue,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    AccountCreationPolicy accountCreation,
    IOptions<WhatsAppLoginOptions> whatsAppOptions)
{
    // El pedido por correo, ya validado: corre dentro del límite de LoginCodeService.RequestLoginCodeAsync.
    public async Task<Result<RequestLoginCodeResponse>> RequestLoginCodeCoreAsync(
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

        var user = await users.FindByEmailAsync(email, cancellationToken);

        // La fila también se guarda para un correo desconocido en InviteOnly: los límites no pueden revelar
        // si existe la cuenta. El administrador inicial puede recibir el email antes de crear su cuenta.
        if (user is not null || await accountCreation.AllowsNewAccountAsync(email, cancellationToken))
        {
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            // Se encola antes del commit para que la fila se confirme ya marcada como enviada. Encolar no espera al
            // SMTP, así que no alarga el lock del destino. Si la cola está llena, el código se guarda como no enviado.
            if (emailQueue.TryEnqueue(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, issued.Value.LifetimeMinutes, culture)))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestLoginCodeResponse(issued.Value.ResendCooldownSeconds);
    }

    // El pedido por WhatsApp, ya validado y con WhatsApp prendido: corre dentro del límite de
    // LoginCodeService.RequestWhatsAppLoginCodeAsync.
    public async Task<Result<RequestWhatsAppLoginCodeResponse>> RequestWhatsAppLoginCodeCoreAsync(
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
            if (sendQueue.TryEnqueue(message))
            {
                issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
            }
        }

        return new RequestWhatsAppLoginCodeResponse(
            issued.Value.ResendCooldownSeconds,
            phone.Value,
            phoneNumbers.Mask(phone));
    }
}
