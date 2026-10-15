namespace ArquitecturaBase.Infrastructure.Files;

internal sealed record FileStorageOptions
{
    public const string SectionName = "FileStorage";
    public bool Enabled { get; init; }
    public string ContentContainer { get; init; } = "files-content";
    public string CredentialMode { get; init; } = "";
    public string ServiceUri { get; init; } = "";
    public string ConnectionString { get; init; } = "";
    public string? TenantId { get; init; }
    public string? ManagedIdentityClientId { get; init; }

    public bool IsValid()
    {
        if (!Enabled) return true;
        if (ContentContainer.Length is < 3 or > 63 || ContentContainer[0] == '-' || ContentContainer[^1] == '-'
            || ContentContainer.Contains("--", StringComparison.Ordinal)
            || ContentContainer.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            return false;
        if (CredentialMode == "ConnectionString") return !string.IsNullOrWhiteSpace(ConnectionString);
        return CredentialMode is "AzureCli" or "VisualStudio" or "ManagedIdentity"
            && string.IsNullOrEmpty(ConnectionString)
            && Uri.TryCreate(ServiceUri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment) && uri.AbsolutePath == "/";
    }

    public override string ToString() => "FileStorageOptions { Credentials = [REDACTED] }";
}
