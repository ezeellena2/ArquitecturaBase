using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
public sealed record AiConnectionResponse(AiProvider Provider, string Model, bool HasCredential, bool Enabled, long Revision)
{
    private static IReadOnlyCollection<AiProvider> SupportedProviders { get; } = [AiProvider.OpenAI, AiProvider.Anthropic];
    public IReadOnlyCollection<AiProvider> Providers { get; } = SupportedProviders;

    /// <summary>I4: the saved provider is one this version does not know; the connection reads empty and paused, with its revision.</summary>
    public bool UnsupportedProvider { get; init; }

    /// <summary>What a saved connection shows: one with a provider this version does not know shows none of its values.</summary>
    public static AiConnectionResponse Of(AiProvider provider, string model, bool hasCredential, bool enabled, long revision) =>
        AiProviders.IsSupported(provider) ? new(provider, model, hasCredential, enabled, revision)
            : new(AiProvider.OpenAI, "", false, false, revision) { UnsupportedProvider = true };
}
