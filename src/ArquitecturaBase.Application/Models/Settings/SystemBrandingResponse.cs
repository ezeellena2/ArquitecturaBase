using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>RC6-B: how the installation presents itself in emails, in the WhatsApp invitation and in the response PDFs.</summary>
public sealed record SystemBrandingResponse(string AppName, string EmailSenderName, string? EmailLogoUrl)
{
    /// <summary>What a database without the settings row presents: the values the installation had in its configuration.</summary>
    public static SystemBrandingResponse Defaults { get; } =
        new(SystemBranding.InitialAppName, SystemBranding.InitialEmailSenderName, null);
}
