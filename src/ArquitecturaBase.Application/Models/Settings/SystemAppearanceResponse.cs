using ArquitecturaBase.Domain.Settings;

namespace ArquitecturaBase.Application.Models.Settings;

public sealed record SystemAppearanceResponse(string PrimaryColor, string MenuColor, string HeaderColor, string AlternateRowColor)
{
    public static SystemAppearanceResponse Defaults { get; } = new(AppearancePalette.InitialPrimaryColor,
        AppearancePalette.InitialMenuColor, AppearancePalette.InitialHeaderColor, AppearancePalette.InitialAlternateRowColor);
}
