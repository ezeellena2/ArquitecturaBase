using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Contracts.Settings;

/// <summary>RC6-B: the name, the sender and the logo, always together; a missing, null or empty logo means none.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SystemBrandingHttpRequest
{
    public required string AppName { get; init; }
    public required string EmailSenderName { get; init; }
    public string? EmailLogoUrl { get; init; }
}
