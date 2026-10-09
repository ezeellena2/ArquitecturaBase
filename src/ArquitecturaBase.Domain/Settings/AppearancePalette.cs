using System.Buffers;

namespace ArquitecturaBase.Domain.Settings;

/// <summary>The four installation colors. Text and interaction colors are derived by the shared UI.</summary>
public sealed record AppearancePalette(string PrimaryColor, string MenuColor, string HeaderColor, string AlternateRowColor)
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");
    public const int ColorLength = 7;
    public const string InitialPrimaryColor = "#285A3D";
    public const string InitialMenuColor = "#24352B";
    public const string InitialHeaderColor = "#DFE8D9";
    public const string InitialAlternateRowColor = "#F4F6F0";

    public static bool IsValidColor(string? color) => color is { Length: ColorLength }
        && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept(HexDigits) < 0;

    public bool IsValid => IsValidColor(PrimaryColor) && IsValidColor(MenuColor)
        && IsValidColor(HeaderColor) && IsValidColor(AlternateRowColor);
}
