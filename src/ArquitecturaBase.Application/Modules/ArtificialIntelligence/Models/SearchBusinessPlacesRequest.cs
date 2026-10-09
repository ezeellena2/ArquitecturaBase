namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record SearchBusinessPlacesRequest(string Input)
{
    public override string ToString() => "SearchBusinessPlacesRequest { input: [REDACTED] }";
}
