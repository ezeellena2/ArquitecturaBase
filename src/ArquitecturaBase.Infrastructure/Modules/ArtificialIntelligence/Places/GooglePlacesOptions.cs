using System.ComponentModel.DataAnnotations;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Places;
public sealed class GooglePlacesOptions
{
    [StringLength(4096), RegularExpression(@"^[!-~]*$")] public string ApiKey { get; set; } = "";
    [Range(1, 15)] public int TimeoutSeconds { get; set; } = 5;
    [RegularExpression("^[A-Za-z]{2}$")] public string? RegionCode { get; set; }
    public override string ToString() => "GooglePlacesOptions { apiKey: [REDACTED] }";
}
