using System.Globalization;
using ArquitecturaBase.Application.Abstractions.Emails;
using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Abstractions.Messaging;
using ArquitecturaBase.Application.Abstractions.Security;
using ArquitecturaBase.Application.Abstractions.Settings;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Settings;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Features.Auth.RequestLoginCode;

/// <summary>
/// Emite un código de ingreso nuevo y encola el email. Aplica el reenvío y el límite por email (sección 5.3); el
/// límite por IP lo aplica el rate limiter de la Api. El email se encola antes de guardar: si el guardado fallara, el
/// usuario recibiría un código que no sirve y pediría otro.
/// En modo InviteOnly, un correo sin cuenta recorre exactamente el mismo camino y lo único que no pasa es el envío
/// del email (sección 4 del spec de la Fase 4).
/// </summary>
internal sealed class RequestLoginCodeCommandHandler(
    ILoginCodeRepository loginCodes,
    IIdentityService identityService,
    ILoginCodeGenerator codeGenerator,
    ILoginCodeHasher codeHasher,
    IEmailTemplateRenderer templateRenderer,
    IEmailQueue emailQueue,
    ISystemSettingsReader systemSettings,
    IOptions<LoginCodeOptions> options,
    TimeProvider timeProvider)
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
        var destination = LoginCodeDestination.ForEmail(email);

        // Los límites de la sección 5.3 se aplican de a un request por email.
        await loginCodes.LockDestinationAsync(destination, cancellationToken);

        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var limitError = await CheckLimitsAsync(destination, settings, nowUtc, cancellationToken);

        if (limitError is not null)
        {
            return limitError;
        }

        foreach (var activeCode in await loginCodes.ListActiveAsync(destination, LoginCodePurpose.SignIn, nowUtc, cancellationToken))
        {
            activeCode.Invalidate(nowUtc);
        }

        var code = codeGenerator.Generate();

        var loginCode = LoginCode.Issue(
            destination,
            LoginCodePurpose.SignIn,
            requestedByUserId: null,
            codeHasher.Hash(destination, LoginCodePurpose.SignIn, code),
            nowUtc,
            TimeSpan.FromMinutes(settings.LifetimeMinutes),
            settings.MaxAttempts);

        loginCodes.Add(loginCode);

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
                templateRenderer.RenderLoginCode(email.Value, code, settings.LifetimeMinutes, culture),
                cancellationToken);

            loginCode.MarkSent(nowUtc);
        }

        return new RequestLoginCodeResponse(settings.ResendCooldownSeconds);
    }

    /// <summary>
    /// Los dos límites son por destino, con cualquier propósito (sección 6.3 del spec del ingreso con WhatsApp):
    /// protegen a quien recibe los mensajes, así que un código que la cuenta pidió desde el perfil para ese mismo
    /// correo también cuenta. Por eso el reenvío mira el último pedido del destino y no el último código de ingreso.
    /// </summary>
    private async Task<Error?> CheckLimitsAsync(
        LoginCodeDestination destination,
        LoginCodeOptions settings,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromMinutes(settings.RequestWindowMinutes);
        var cooldown = TimeSpan.FromSeconds(settings.ResendCooldownSeconds);

        // Una sola consulta para los dos límites, que cubre el más largo de los dos plazos: se configuran por separado.
        var requestTimes = await loginCodes.ListRequestTimesSinceAsync(
            destination, nowUtc - (window > cooldown ? window : cooldown), cancellationToken);

        var windowStartUtc = nowUtc - window;
        var requestTimesInWindow = requestTimes.Where(requestedAtUtc => requestedAtUtc > windowStartUtc).ToList();

        if (requestTimesInWindow.Count >= settings.MaxRequestsPerWindow)
        {
            // Se libera un lugar cuando el pedido más viejo de la ventana sale de ella.
            return LoginCodeErrors.TooManyRequests(SecondsUntil(requestTimesInWindow[0] + window, nowUtc));
        }

        var resendAllowedAtUtc = requestTimes.Count > 0 ? requestTimes[^1] + cooldown : (DateTime?)null;

        return resendAllowedAtUtc > nowUtc
            ? LoginCodeErrors.ResendTooSoon(SecondsUntil(resendAllowedAtUtc.Value, nowUtc))
            : null;
    }

    private static int SecondsUntil(DateTime momentUtc, DateTime nowUtc) =>
        Math.Max(1, (int)Math.Ceiling((momentUtc - nowUtc).TotalSeconds));
}
