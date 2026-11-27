using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Contracts.Settings;

/// <summary>RC6-B: the name and the sender, always together. The logo is uploaded apart (ME2b).</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SystemBrandingHttpRequest
{
    public required string AppName { get; init; }
    public required string EmailSenderName { get; init; }
}
