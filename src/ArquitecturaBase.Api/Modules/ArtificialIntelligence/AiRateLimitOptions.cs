using System.ComponentModel.DataAnnotations;
namespace ArquitecturaBase.Api.Modules.ArtificialIntelligence;
public sealed class AiRateLimitOptions
{
    [Range(1, 100000)] public int ReadPermitLimit { get; set; } = 120;
    [Range(1, 100000)] public int WritePermitLimit { get; set; } = 20;
    [Range(1, 100000)] public int TestPermitLimit { get; set; } = 4;
    [Range(1, 100000)] public int PlacesPermitLimit { get; set; } = 60;
    [Range(1, 100000)] public int AssistantSubmitPermitLimit { get; set; } = 6;
}
