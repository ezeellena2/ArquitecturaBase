using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Api.Modules.WhatsApp;

internal sealed class WhatsAppChatRateLimitOptions
{
    public const string SectionName = "RateLimiting";
    [Range(1, 100000)]
    public int WhatsAppChatReadPermitLimit { get; init; } = 600;
    [Range(1, 100000)]
    public int WhatsAppChatWritePermitLimit { get; init; } = 120;
    /// <summary>Consumo: each read goes to Meta's management quota, the same Flows use, so it is kept low per operator.</summary>
    [Range(1, 1000)]
    public int WhatsAppUsagePermitLimit { get; init; } = 10;
}
