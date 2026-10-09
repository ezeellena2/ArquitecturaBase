using System.Text.Json.Serialization;

namespace ArquitecturaBase.Api.Contracts.Settings;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SystemAppearanceHttpRequest
{
    public required string PrimaryColor { get; init; }
    public required string MenuColor { get; init; }
    public required string HeaderColor { get; init; }
    public required string AlternateRowColor { get; init; }
}
