using System.ComponentModel.DataAnnotations;
namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
public sealed class AiTransportOptions
{
    [Range(1, 60)] public int TimeoutSeconds { get; set; } = 15;
    [Range(1024, 262144)] public int MaxResponseBytes { get; set; } = 65536;
    [Range(1, 8)] public int MaxConcurrentTests { get; set; } = 2;
    [Range(64, 1024)] public int MaxOutputTokens { get; set; } = 128;
}
