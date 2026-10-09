namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence.Contracts;
public sealed record SearchBusinessPlacesHttpRequest(string Input)
{
    public override string ToString() => "SearchBusinessPlacesHttpRequest { input: [REDACTED] }";
}
