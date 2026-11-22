namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>RC1-A: the three sign-in periods saved together.</summary>
public sealed record IdentityRetentionRequest(int LoginAuditMonths, int LoginCodeDays, int InvitationDays);
