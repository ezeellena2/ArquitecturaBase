using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
/// <summary>
/// The connection the screen shows. RC1-A: ConversationRetentionDays is how many days Home conversations are kept, at most
/// ConversationRetentionCeilingDays, the installation's ceiling (ArtificialIntelligence:Assistant:Limits:RetentionDays).
/// </summary>
public sealed record AiConnectionResponse(AiProvider Provider, string Model, bool HasCredential, bool Enabled, long Revision,
    int ConversationRetentionDays, int ConversationRetentionCeilingDays)
{
    private static IReadOnlyCollection<AiProvider> SupportedProviders { get; } = [AiProvider.OpenAI, AiProvider.Anthropic];
    public IReadOnlyCollection<AiProvider> Providers { get; } = SupportedProviders;

    /// <summary>I4: the saved provider is one this version does not know; the connection reads empty and paused, with its revision.</summary>
    public bool UnsupportedProvider { get; init; }

    /// <summary>
    /// What a saved connection shows: one with a provider this version does not know shows none of its values. The retention shows
    /// what applies, the saved days never beyond the ceiling, so the screen starts from a valid value if the ceiling was lowered.
    /// </summary>
    public static AiConnectionResponse Of(AiProvider provider, string model, bool hasCredential, bool enabled, long revision,
        int conversationRetentionDays, int retentionCeilingDays)
    {
        var days = Math.Clamp(conversationRetentionDays, 1, retentionCeilingDays);
        return AiProviders.IsSupported(provider) ? new(provider, model, hasCredential, enabled, revision, days, retentionCeilingDays)
            : new(AiProvider.OpenAI, "", false, false, revision, days, retentionCeilingDays) { UnsupportedProvider = true };
    }
}
