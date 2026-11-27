using ArquitecturaBase.Domain.Common;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

/// <summary>
/// PR13 (2026-11-27): the prices of a model that the administrator copies from the provider's list, to estimate Consumo. Each
/// price is optional (a model may bill only tokens or only minutes); without one, what it measures does not get a cost.
/// </summary>
public sealed class AiModelPrice : Entity, IAuditable
{
    public const int MaximumModelLength = 150;
    public const decimal MaximumUsdPerMillionTokens = 1000m;
    public const decimal MaximumUsdPerMinute = 10m;
    public const int MaximumDecimals = 6;

    private AiModelPrice() { }
    public AiProvider Provider { get; private set; }
    public string Model { get; private set; } = string.Empty;
    public decimal? InputUsdPerMillion { get; private set; }
    public decimal? OutputUsdPerMillion { get; private set; }
    public decimal? AudioUsdPerMinute { get; private set; }
    public long Revision { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? ModifiedAtUtc { get; private set; }
    public Guid? ModifiedBy { get; private set; }

    public static bool IsValidModel(string? model) =>
        model?.Trim() is { Length: > 0 and <= MaximumModelLength } trimmed && !trimmed.Any(char.IsControl);

    public static bool IsValidTokenPrice(decimal? price) => IsValid(price, MaximumUsdPerMillionTokens);

    public static bool IsValidAudioPrice(decimal? price) => IsValid(price, MaximumUsdPerMinute);

    public static Result<AiModelPrice> Create(AiProvider provider, string model, decimal? inputUsdPerMillion, decimal? outputUsdPerMillion,
        decimal? audioUsdPerMinute)
    {
        if (!IsValidModel(model) || !AreValid(inputUsdPerMillion, outputUsdPerMillion, audioUsdPerMinute)) return AiModelPriceErrors.Invalid;
        return new AiModelPrice
        {
            Provider = provider, Model = model.Trim(), InputUsdPerMillion = inputUsdPerMillion, OutputUsdPerMillion = outputUsdPerMillion,
            AudioUsdPerMinute = audioUsdPerMinute,
        };
    }

    public Result Update(long expectedRevision, decimal? inputUsdPerMillion, decimal? outputUsdPerMillion, decimal? audioUsdPerMinute)
    {
        if (expectedRevision != Revision) return AiModelPriceErrors.RevisionConflict;
        if (!AreValid(inputUsdPerMillion, outputUsdPerMillion, audioUsdPerMinute)) return AiModelPriceErrors.Invalid;
        if (InputUsdPerMillion == inputUsdPerMillion && OutputUsdPerMillion == outputUsdPerMillion && AudioUsdPerMinute == audioUsdPerMinute)
            return Result.Success();
        InputUsdPerMillion = inputUsdPerMillion;
        OutputUsdPerMillion = outputUsdPerMillion;
        AudioUsdPerMinute = audioUsdPerMinute;
        Revision++;
        return Result.Success();
    }

    private static bool AreValid(decimal? input, decimal? output, decimal? audio) =>
        IsValidTokenPrice(input) && IsValidTokenPrice(output) && IsValidAudioPrice(audio);

    private static bool IsValid(decimal? price, decimal maximum) =>
        price is not { } value || (value >= 0 && value <= maximum && decimal.Round(value, MaximumDecimals) == value);
}
