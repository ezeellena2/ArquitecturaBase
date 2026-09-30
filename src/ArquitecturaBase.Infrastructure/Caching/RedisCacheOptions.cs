using System.ComponentModel.DataAnnotations;

namespace ArquitecturaBase.Infrastructure.Caching;

internal sealed class RedisCacheOptions
{
    public const string SectionName = "Caching";

    public string KeyPrefix { get; set; } = string.Empty;

    [Range(1, 16_777_216)]
    public int MaximumPayloadBytes { get; set; } = 1_048_576;

    [Range(1, 60)]
    public int LeaseSeconds { get; set; } = 15;

    [Range(1, 120)]
    public int WaitSeconds { get; set; } = 30;

    [Range(1, 1_000)]
    public int RetryMilliseconds { get; set; } = 25;
}
