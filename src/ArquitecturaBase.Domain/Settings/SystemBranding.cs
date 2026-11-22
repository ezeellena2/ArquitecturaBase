namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// RC6-B: how the installation presents itself in emails, in the WhatsApp invitation and in the response PDFs: its name, the
/// sender's name and the logo of the emails. The sending account and address stay in the installation's configuration.
/// </summary>
public sealed record SystemBranding(string AppName, string EmailSenderName, string? EmailLogoUrl)
{
    // The values the installation had in Email:AppName and Email:Smtp:FromName, without a logo.
    public const string InitialAppName = "Acceso";
    public const string InitialEmailSenderName = "Acceso";

    // The name travels as a WhatsApp template parameter and in a PDF heading: one short line.
    public const int MaxNameLength = 60;
    public const int MaxLogoUrlLength = 2048;

    /// <summary>One line with text, without control characters, up to <see cref="MaxNameLength"/> once trimmed.</summary>
    public static bool IsValidName(string? name) =>
        name?.Trim() is { Length: > 0 and <= MaxNameLength } trimmed && !trimmed.Any(char.IsControl);

    /// <summary>
    /// Empty means no logo. Otherwise an absolute https address, without spaces, of a public image: mail clients block other
    /// schemes and Gmail does not show SVG.
    /// </summary>
    public static bool IsValidLogoUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return true;
        var trimmed = url.Trim();
        return trimmed.Length <= MaxLogoUrlLength && !trimmed.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            && Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Length > 0;
    }

    public bool IsValid => IsValidName(AppName) && IsValidName(EmailSenderName) && IsValidLogoUrl(EmailLogoUrl);

    /// <summary>The names trimmed and the logo trimmed, or none when empty. Only for a valid branding.</summary>
    public SystemBranding Normalized() => new(AppName.Trim(), EmailSenderName.Trim(),
        string.IsNullOrWhiteSpace(EmailLogoUrl) ? null : EmailLogoUrl.Trim());
}
