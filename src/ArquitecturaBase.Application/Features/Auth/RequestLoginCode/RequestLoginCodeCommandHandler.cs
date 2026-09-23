using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>
/// Emite un código de ingreso nuevo y encola el email. El lock, el reenvío y el límite por email (sección 5.3) los
/// aplica <see cref="LoginCodeIssuer"/>, el mismo que usa el pedido por WhatsApp. El email se encola antes de guardar:
/// si el guardado fallara, el usuario recibiría un código que no sirve y pediría otro.
/// En modo InviteOnly, un correo sin cuenta recorre exactamente el mismo camino y lo único que no pasa es el envío
/// del email (sección 4 del spec de la Fase 4).
/// </summary>
internal sealed class RequestLoginCodeCommandHandler(
    LoginCodeIssuer issuer,
    IIdentityService identityService,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    ISystemSettingsReader systemSettings,
    IOptions<LoginCodeOptions> options)
    : ICommandHandler<RequestLoginCodeCommand, RequestLoginCodeResponse>
{
    public async Task<Result<RequestLoginCodeResponse>> Handle(RequestLoginCodeCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);

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

        var settings = options.Value;
        var user = await identityService.FindByEmailAsync(email, cancellationToken);

        // InviteOnly: a un correo sin cuenta se le emitió el código igual, pero no se le manda ningún email, y la
        // respuesta es la misma de siempre. El código se emite a propósito: los límites por dirección se apoyan en
        // esta fila, y sin ella una dirección desconocida respondería 202 para siempre mientras una registrada
        // empieza a responder 429, que es todo lo que hace falta para enumerar cuentas (sección 4 del spec de la
        // Fase 4). La fila vence sola a los 10 minutos sin que nadie la use, y queda sin fecha de envío.
        if (user is not null || await systemSettings.GetRegistrationModeAsync(cancellationToken) is RegistrationMode.Open)
        {
            // El email sale en el idioma del perfil; si la cuenta todavía no existe, en el de la petición.
            var culture = user is null ? CultureInfo.CurrentUICulture : CultureInfo.GetCultureInfo(user.Culture);

            await emailQueue.EnqueueAsync(
                templateRenderer.RenderLoginCode(email.Value, issued.Value.Code, settings.LifetimeMinutes, culture),
                cancellationToken);

            issued.Value.LoginCode.MarkSent(issued.Value.IssuedAtUtc);
        }

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }
}
