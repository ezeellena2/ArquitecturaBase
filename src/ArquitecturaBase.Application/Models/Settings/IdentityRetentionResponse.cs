using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>RC1-A: how long the sign-in data are kept; the retention reads them on every run.</summary>
public sealed record IdentityRetentionResponse(int LoginAuditMonths, int LoginCodeDays, int InvitationDays)
{
    /// <summary>What a database without the settings row keeps: the periods decided on 2026-11-14.</summary>
    public static IdentityRetentionResponse Defaults { get; } = new(IdentityRetentionPeriods.InitialLoginAuditMonths,
        IdentityRetentionPeriods.InitialLoginCodeDays, IdentityRetentionPeriods.InitialInvitationDays);

    public IdentityRetentionPeriods ToPeriods() => new(LoginAuditMonths, LoginCodeDays, InvitationDays);
}
