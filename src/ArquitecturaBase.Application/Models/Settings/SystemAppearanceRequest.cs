namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>The five colors and the font, always together (ME3-B, ME4).</summary>
public sealed record SystemAppearanceRequest(string PrimaryColor, string MenuColor, string HeaderColor, string AlternateRowColor,
    string MenuAccentColor, string Font);
