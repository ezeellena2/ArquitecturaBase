using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>
/// RC6-B: how the installation presents itself in emails, in the WhatsApp invitation and in the response PDFs. The logo and the
/// icon go as the version of their file (ME2), or null without them: their public address is
/// <c>api/settings/brand/logo?v={version}</c>.
/// </summary>
public sealed record SystemBrandingResponse(string AppName, string EmailSenderName, string? LogoVersion, string? IconVersion)
{
    /// <summary>What a database without the settings row presents: the values the installation had in its configuration.</summary>
    public static SystemBrandingResponse Defaults { get; } =
        new(SystemBranding.InitialAppName, SystemBranding.InitialEmailSenderName, null, null);

    public static SystemBrandingResponse Of(string appName, string senderName, string? logoKey, string? iconKey) =>
        new(appName, senderName, VersionOf(logoKey), VersionOf(iconKey));

    internal static string? VersionOf(string? key) => BrandImage.IsAnyKey(key) ? BrandImage.VersionOf(key) : null;
}
