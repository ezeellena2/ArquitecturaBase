namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// RC6-B: how the installation presents itself in emails, in the WhatsApp invitation and in the response PDFs: its name and the
/// sender's name. The sending account and address stay in the installation's configuration; the logo is an uploaded file
/// (<see cref="BrandImage"/>, ME2).
/// </summary>
public sealed record SystemBranding(string AppName, string EmailSenderName)
{
    // The values the installation had in Email:AppName and Email:Smtp:FromName.
    public const string InitialAppName = "Acceso";
    public const string InitialEmailSenderName = "Acceso";

    // The name travels as a WhatsApp template parameter and in a PDF heading: one short line.
    public const int MaxNameLength = 60;

    /// <summary>One line with text, without control characters, up to <see cref="MaxNameLength"/> once trimmed.</summary>
    public static bool IsValidName(string? name) =>
        name?.Trim() is { Length: > 0 and <= MaxNameLength } trimmed && !trimmed.Any(char.IsControl);

    public bool IsValid => IsValidName(AppName) && IsValidName(EmailSenderName);

    /// <summary>The names trimmed. Only for a valid branding.</summary>
    public SystemBranding Normalized() => new(AppName.Trim(), EmailSenderName.Trim());
}
