using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Contracts.Settings;

/// <summary>RC1-A: the three sign-in periods, always together.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record IdentityRetentionHttpRequest
{
    public required int LoginAuditMonths { get; init; }
    public required int LoginCodeDays { get; init; }
    public required int InvitationDays { get; init; }
}
