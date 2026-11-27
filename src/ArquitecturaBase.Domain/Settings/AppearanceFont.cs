namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// ME4: the font of the whole application, from a closed list the front ships. The codes are stable: the front maps each one to
/// its family, and a new font is a new code here and a new package there.
/// </summary>
public static class AppearanceFont
{
    public const string Initial = "public-sans";
    public const int MaxLength = 32;

    public static IReadOnlyList<string> Supported { get; } =
        ["public-sans", "inter", "ibm-plex-sans", "source-sans-3", "hanken-grotesk", "nunito-sans"];

    public static bool IsSupported(string? font) => font is not null && Supported.Contains(font, StringComparer.Ordinal);
}
