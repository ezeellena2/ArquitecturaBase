using ArquitecturaBase.Application.Configuration.Auth;
using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Services.Auth;

/// <summary>
/// N16: desde cuándo es viejo cada dato del ingreso, a la hora de la corrida (identidad.md, «Retención de los datos del
/// ingreso»). Lo anterior a cada fecha se borra o se limpia; las fechas son exclusivas. Los plazos de los ingresos, los
/// códigos y las invitaciones son los de la configuración en esa corrida (RC1-A); el de las sesiones sigue en la instalación.
/// </summary>
internal static class IdentityRetentionPolicy
{
    public static IdentityRetentionCutoffs CutoffsAt(DateTime nowUtc, IdentityRetentionPeriods periods, IdentityRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(periods);
        ArgumentNullException.ThrowIfNull(options);

        if (nowUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The retention runs on UTC time.", nameof(nowUtc));
        }

        // Los meses son de calendario, como los decidió el usuario: «12 meses», no 360 días.
        return new IdentityRetentionCutoffs(
            nowUtc.AddDays(-periods.LoginCodeDays),
            nowUtc.AddMonths(-periods.LoginAuditMonths),
            nowUtc.AddDays(-periods.InvitationDays),
            SessionsExpiredBeforeUtc(nowUtc, options));
    }

    /// <summary>Las autorizaciones y los tokens de OpenIddict vencidos antes de esta fecha se podan: su plazo es de la instalación.</summary>
    public static DateTime SessionsExpiredBeforeUtc(DateTime nowUtc, IdentityRetentionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return nowUtc.AddDays(-options.SessionDaysAfterExpiry);
    }
}

/// <param name="LoginCodesBeforeUtc">Los códigos y los enlaces creados antes se borran.</param>
/// <param name="LoginAuditsBeforeUtc">Los ingresos anteriores quedan sin la IP ni el navegador.</param>
/// <param name="InvitationsBeforeUtc">Las invitaciones mandadas antes se borran, salvo la última de cada cuenta.</param>
/// <param name="SessionsExpiredBeforeUtc">Las autorizaciones y los tokens de OpenIddict vencidos antes se podan.</param>
internal sealed record IdentityRetentionCutoffs(
    DateTime LoginCodesBeforeUtc, DateTime LoginAuditsBeforeUtc, DateTime InvitationsBeforeUtc, DateTime SessionsExpiredBeforeUtc);
