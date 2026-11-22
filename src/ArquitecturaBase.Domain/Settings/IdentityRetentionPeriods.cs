namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// RC1-A: how long the sign-in data are kept (identidad.md, «Retención de los datos del ingreso»). The defaults are the periods the
/// user decided on 2026-11-14; the privacy policy has to promise the same ones.
/// </summary>
/// <param name="LoginAuditMonths">After these calendar months a sign-in record stays without its IP and browser.</param>
/// <param name="LoginCodeDays">After these days the login codes are removed.</param>
/// <param name="InvitationDays">After these days the invitations that are no longer the last one of their account are removed.</param>
public sealed record IdentityRetentionPeriods(int LoginAuditMonths, int LoginCodeDays, int InvitationDays)
{
    public const int InitialLoginAuditMonths = 12;
    public const int InitialLoginCodeDays = 30;
    public const int InitialInvitationDays = 30;

    public const int MinLoginAuditMonths = 1;
    public const int MaxLoginAuditMonths = 120;

    // At least a day: the limits that read the login codes look at the last 24 hours.
    public const int MinLoginCodeDays = 1;
    public const int MaxLoginCodeDays = 3650;

    // At least seven days: the delivery of an invitation lives a day, and a shorter period could remove one still on its way.
    public const int MinInvitationDays = 7;
    public const int MaxInvitationDays = 3650;

    public static IdentityRetentionPeriods Initial { get; } = new(InitialLoginAuditMonths, InitialLoginCodeDays, InitialInvitationDays);

    public bool IsValid => LoginAuditMonths is >= MinLoginAuditMonths and <= MaxLoginAuditMonths
        && LoginCodeDays is >= MinLoginCodeDays and <= MaxLoginCodeDays
        && InvitationDays is >= MinInvitationDays and <= MaxInvitationDays;
}
