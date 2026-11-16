namespace ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

public enum AiProvider { OpenAI, Anthropic }

/// <summary>
/// I4: a provider name that a newer version saved and this one does not know reads as <see cref="Unsupported"/>, outside the enum, instead
/// of failing every read. It is never offered, sent or written: the connection reads paused and the assistant unavailable until someone
/// chooses a provider again, and a turn sealed with it is left for the version that knows it.
/// </summary>
public static class AiProviders
{
    public const AiProvider Unsupported = (AiProvider)(-1);

    public static bool IsSupported(AiProvider provider) => Enum.IsDefined(provider);
}
