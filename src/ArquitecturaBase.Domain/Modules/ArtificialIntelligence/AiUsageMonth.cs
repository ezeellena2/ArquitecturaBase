using ArquitecturaBase.Domain.Common;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

/// <summary>
/// PR12 (2026-11-27): what the turns of a business month sent to one model, kept after the retention deletes those turns. The
/// retention adds a conversation's turns here in the same unit that removes it; Consumo adds the turns still alive. An amount no
/// turn reported stays null: unknown, never zero.
/// </summary>
public sealed class AiUsageMonth : Entity
{
    private AiUsageMonth() { }
    public DateOnly Month { get; private set; }
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public long Requests { get; private set; }
    public long UnknownUsageRequests { get; private set; }
    public long? InputTokens { get; private set; }
    public long? OutputTokens { get; private set; }
    public decimal? AudioSeconds { get; private set; }

    public static AiUsageMonth Create(DateOnly month, AiProvider provider, string model) =>
        new() { Month = new DateOnly(month.Year, month.Month, 1), Provider = provider, Model = model };

    public void Add(long requests, long unknownUsageRequests, long? inputTokens, long? outputTokens, decimal? audioSeconds)
    {
        Requests = checked(Requests + requests);
        UnknownUsageRequests = checked(UnknownUsageRequests + unknownUsageRequests);
        if (inputTokens is { } input) InputTokens = checked((InputTokens ?? 0) + input);
        if (outputTokens is { } output) OutputTokens = checked((OutputTokens ?? 0) + output);
        if (audioSeconds is { } seconds) AudioSeconds = (AudioSeconds ?? 0) + seconds;
    }
}
