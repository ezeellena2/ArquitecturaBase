namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Security;
public sealed record AiSecretProtectionOptions
{
    public string InstallationId { get; init; } = string.Empty;
    public string ActiveKeyId { get; init; } = string.Empty;
    public Dictionary<string, string> Keys { get; init; } = new(StringComparer.Ordinal);
    public override string ToString() => "AiSecretProtectionOptions { keys: [REDACTED] }";
    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(InstallationId) || InstallationId.Length > 100 || Keys.Count == 0 || !Keys.ContainsKey(ActiveKeyId)) return false;
        return Keys.All(x => x.Key.Length is > 0 and <= 32 && x.Key.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && ValidKey(x.Value));
    }
    private static bool ValidKey(string key)
    {
        Span<byte> bytes = stackalloc byte[32];
        return Convert.TryFromBase64String(key, bytes, out var length) && length == 32;
    }
}
