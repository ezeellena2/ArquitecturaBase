namespace ArquitecturaBase.Application.Models.Assistant;

/// <summary>Server-owned private object, never accepted from HTTP or model arguments.</summary>
public sealed record AssistantAudioInput(string StorageKey, string Name, string ContentType, long Size,
    string Sha256, DateTime ExpiresAtUtc)
{
    public override string ToString() => "AssistantAudioInput { Content = [REDACTED] }";
}

public sealed record AssistantNormalizedAudio(string Text, IReadOnlyList<string> Languages)
{
    public override string ToString() => "AssistantNormalizedAudio { Content = [REDACTED] }";
}
