using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

/// <summary>The five colors and the font. The menu accent is always the effective one: without a saved accent, the derived.</summary>
public sealed record SystemAppearanceResponse(string PrimaryColor, string MenuColor, string HeaderColor, string AlternateRowColor,
    string MenuAccentColor, string Font)
{
    public static SystemAppearanceResponse Defaults { get; } = new(AppearancePalette.InitialPrimaryColor,
        AppearancePalette.InitialMenuColor, AppearancePalette.InitialHeaderColor, AppearancePalette.InitialAlternateRowColor,
        AppearancePalette.InitialMenuAccentColor, AppearanceFont.Initial);

    /// <summary>With the accent resolved: the saved one, or the one derived from the menu.</summary>
    public static SystemAppearanceResponse Of(string primary, string menu, string header, string alternate, string? accent, string font) =>
        new(primary, menu, header, alternate, accent ?? AppearancePalette.DeriveMenuAccent(menu), font);
}
