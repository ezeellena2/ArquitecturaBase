namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record BusinessPlaceResponse(string PlaceId, string Label)
{
    public override string ToString() => "BusinessPlaceResponse { content: [REDACTED] }";
}
public sealed record BusinessPlacesResponse(IReadOnlyList<BusinessPlaceResponse> Places);
