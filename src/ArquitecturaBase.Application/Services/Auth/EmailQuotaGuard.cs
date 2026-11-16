using ArquitecturaBase.Application.Common.RateLimiting;
using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Application.Interfaces.Integrations.Emails;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// N7: los topes diarios de lo que sale por correo, que es el H10 del correo (<c>WhatsAppCodeQuotaGuard</c> en su módulo).
/// Cuentan en una ventana móvil de 24 horas sobre filas de la base (<see cref="Domain.Authentication.LoginCode"/> y
/// <see cref="UserInvitation"/>), así que valen para todas las copias y sobreviven a un reinicio:
/// <list type="bullet">
/// <item>por dirección, todo código por correo pedido desde la del pedido (agrupada con <see cref="AddressPartition"/>),
/// para entrar o desde el perfil, haya salido o no: en InviteOnly uno a un correo sin cuenta no sale, y si no contara, el
/// tope diría qué correos tienen cuenta;</item>
/// <item>por cuenta, los códigos por correo que salieron para el perfil de quien los pide;</item>
/// <item>por administrador, sus invitaciones por correo;</item>
/// <item>el de todos, lo que salió por correo con un código o una invitación, por debajo del cupo de la cuenta que manda,
/// con un aviso en el log al 80 % y otro al tope, cada uno una vez por día en cada copia (<see cref="IEmailQuotaWarnings"/>).
/// Los códigos frenan antes, en el tope menos <see cref="EmailQuotaOptions.DailyInvitationReserve"/>, y las invitaciones
/// llegan al tope: un abuso anónimo de los códigos no deja a los administradores sin invitar. Los avisos miran el tope.</item>
/// </list>
/// Los tres de los códigos responden lo mismo, <see cref="LoginCodeErrors.DailyLimitReached"/>, sea cual sea el destino.
/// <para>
/// El de la dirección no dice qué correos tienen cuenta; el de todos sí puede decirlo, en InviteOnly. Sube sólo con lo que
/// sale, y un código a un correo sin cuenta no sale: con un único lugar libre para los códigos, pedir uno para un correo y
/// después otro cualquiera responde 429 si el correo tenía cuenta y 202 si no. Es un costo aceptado (2026-11-16, N7): para
/// usarlo hay que llenar los lugares de los códigos (350 por defecto) con códigos a cuentas conocidas desde 8 o más
/// direcciones (cada una pide hasta 50 por día), unos 400 pedidos por día, mientras nadie recibe códigos por correo y salta
/// el aviso del 80 %. No se cuenta de otro modo: sumar los que no salen dejaría llenar el tope con correos inventados, sin
/// conocer ninguna cuenta ni gastar el cupo de la cuenta que manda.
/// </para>
/// Son aproximados: se cuenta antes de emitir y sin lock, así que pedidos simultáneos en el borde pueden pasarse por los
/// que están en curso (los acota el rate limiter de cada ruta). Lo llaman <see cref="SignInCodeIssuer"/>,
/// <see cref="DestinationCodeIssuer"/> y <c>UserInvitationIssuer</c> antes de emitir, dentro del límite de su punto de
/// entrada; no guarda.
/// </summary>
internal sealed partial class EmailQuotaGuard(
    ILoginCodeRepository loginCodes,
    IUserInvitationRepository invitations,
    IRequestInfo request,
    IOptions<EmailQuotaOptions> options,
    IEmailQuotaWarnings warnings,
    TimeProvider timeProvider,
    ILogger<EmailQuotaGuard> logger)
{
    /// <summary>Los topes cuentan en una ventana móvil de 24 horas, no por día calendario.</summary>
    private static readonly TimeSpan DailyWindow = TimeSpan.FromDays(1);

    /// <summary>
    /// Un código por correo: el tope de la dirección del pedido, el de la cuenta si lo pide una desde su perfil
    /// (<paramref name="requestedByUserId"/>; null para entrar) y el de todos. Null si queda lugar en los tres.
    /// </summary>
    public async Task<Error?> CheckCodeAsync(Guid? requestedByUserId, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (AddressPartition.OfKnown(request.IpAddress) is { } address)
        {
            var requested = await loginCodes.ListLatestRequestTimesFromAddressAsync(
                LoginCodeChannel.Email, address, nowUtc - DailyWindow, settings.DailyCodeLimitPerAddress, cancellationToken);
            if (WaitSeconds(requested, settings.DailyCodeLimitPerAddress, nowUtc) is { } addressWait)
            {
                LogAddressLimitReached(logger, settings.DailyCodeLimitPerAddress);
                return LoginCodeErrors.DailyLimitReached(addressWait);
            }
        }

        if (requestedByUserId is { } userId)
        {
            var sent = await loginCodes.ListLatestSentTimesByRequesterAsync(
                LoginCodeChannel.Email, userId, nowUtc - DailyWindow, settings.DailyProfileCodeLimitPerAccount, cancellationToken);
            if (WaitSeconds(sent, settings.DailyProfileCodeLimitPerAccount, nowUtc) is { } accountWait)
            {
                LogAccountLimitReached(logger, settings.DailyProfileCodeLimitPerAccount);
                return LoginCodeErrors.DailyLimitReached(accountWait);
            }
        }

        return await CheckEveryoneAsync(settings.DailyLimit - settings.DailyInvitationReserve, nowUtc, cancellationToken) is { } wait
            ? LoginCodeErrors.DailyLimitReached(wait)
            : null;
    }

    /// <summary>
    /// Una invitación por correo que manda <paramref name="sentBy"/>: su tope y el de todos, hasta el tope entero (la reserva
    /// es de las invitaciones).
    /// </summary>
    public async Task<Error?> CheckInvitationAsync(Guid sentBy, CancellationToken cancellationToken)
    {
        var limit = options.Value.DailyInvitationLimitPerAdministrator;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        var sent = await invitations.ListLatestSentTimesBySenderAsync(
            UserInvitationChannel.Email, sentBy, nowUtc - DailyWindow, limit, cancellationToken);
        if (WaitSeconds(sent, limit, nowUtc) is { } administratorWait)
        {
            LogAdministratorLimitReached(logger, limit);
            return UserInvitationErrors.DailyLimitReached(administratorWait);
        }

        return await CheckEveryoneAsync(options.Value.DailyLimit, nowUtc, cancellationToken) is { } wait
            ? UserInvitationErrors.DailyLimitReached(wait)
            : null;
    }

    // Los códigos y las invitaciones que salieron por correo, juntos: es lo que gasta el cupo de la cuenta que manda. Se
    // leen hasta el tope de cada lado, se juntan del más nuevo al más viejo y se cortan en el tope. Quien pide llega hasta
    // reachUpTo (los códigos, el tope menos la reserva; las invitaciones, el tope), y los avisos miran el tope.
    private async Task<int?> CheckEveryoneAsync(int reachUpTo, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var limit = settings.DailyLimit;
        var sinceUtc = nowUtc - DailyWindow;

        var codes = await loginCodes.ListLatestSentTimesAsync(LoginCodeChannel.Email, sinceUtc, limit, cancellationToken);
        var invited = await invitations.ListLatestSentTimesAsync(UserInvitationChannel.Email, sinceUtc, limit, cancellationToken);
        var sent = codes.Concat(invited).OrderDescending().Take(limit).ToList();

        if (sent.Count >= limit)
        {
            if (warnings.TryWarnReached(nowUtc))
            {
                LogDailyLimitReached(logger, limit);
            }
        }
        else if (sent.Count >= WarningThreshold(limit) && warnings.TryWarnNear(nowUtc))
        {
            LogDailyLimitNear(logger, sent.Count, limit, limit - settings.DailyInvitationReserve);
        }

        return WaitSeconds(sent, reachUpTo, nowUtc);
    }

    // Vienen del más nuevo al más viejo, hasta el tope: con el tope lleno, se libera un lugar cuando el último de la lista
    // sale de la ventana.
    private static int? WaitSeconds(IReadOnlyList<DateTime> newestFirst, int limit, DateTime nowUtc) =>
        newestFirst.Count < limit ? null : LoginCodeIssuer.SecondsUntil(newestFirst[limit - 1] + DailyWindow, nowUtc);

    private static int WarningThreshold(int limit) => (int)Math.Ceiling(limit * 0.8m);

    // Sin la dirección, la cuenta ni el administrador: sólo que pasó.
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An address reached its daily limit of {Limit} codes by email; no code is sent for it until its oldest one leaves the window")]
    private static partial void LogAddressLimitReached(ILogger logger, int limit);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An account reached its daily limit of {Limit} codes by email from its profile; no code is sent for it until its oldest one leaves the window")]
    private static partial void LogAccountLimitReached(ILogger logger, int limit);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "An administrator reached the daily limit of {Limit} invitations by email; no invitation is sent by email for them until their oldest one leaves the window")]
    private static partial void LogAdministratorLimitReached(ILogger logger, int limit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The emails with a code or an invitation of the last 24 hours reached {Sent} of the daily limit of {Limit}; codes by email stop at {CodeLimit} and invitations at the limit")]
    private static partial void LogDailyLimitNear(ILogger logger, int sent, int limit, int codeLimit);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The emails with a code or an invitation of the last 24 hours reached the daily limit of {Limit}; no code or invitation is sent by email until the oldest one leaves the window")]
    private static partial void LogDailyLimitReached(ILogger logger, int limit);
}

