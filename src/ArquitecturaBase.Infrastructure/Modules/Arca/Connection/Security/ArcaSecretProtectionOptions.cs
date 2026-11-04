namespace ArquitecturaBase.Infrastructure.Modules.Arca.Connection.Security;

/// El anillo externo del módulo (ADR 0023): el mismo diseño que el de IA (ADR 0014), con su propia sección, porque los
/// módulos no comparten código. Va en user-secrets o variables de entorno, nunca en la base ni en appsettings.
public sealed record ArcaSecretProtectionOptions
{
    public string InstallationId { get; init; } = string.Empty;
    public string ActiveKeyId { get; init; } = string.Empty;
    public Dictionary<string, string> Keys { get; init; } = new(StringComparer.Ordinal);
    public override string ToString() => "ArcaSecretProtectionOptions { keys: [REDACTED] }";
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
