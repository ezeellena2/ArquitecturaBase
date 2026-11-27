using System.Buffers;
using System.Globalization;

namespace ArquitecturaBase.Domain.Settings;

/// <summary>
/// The five installation colors. Text and interaction colors are derived by the shared UI; the menu accent (ME3-B) marks the
/// current item of the main menu, its focus and its avatar, and paints the current item's text, so it must read as text on
/// the menu color.
/// </summary>
public sealed record AppearancePalette(string PrimaryColor, string MenuColor, string HeaderColor, string AlternateRowColor,
    string MenuAccentColor)
{
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");
    public const int ColorLength = 7;
    public const string InitialPrimaryColor = "#285A3D";
    public const string InitialMenuColor = "#24352B";
    public const string InitialHeaderColor = "#DFE8D9";
    public const string InitialAlternateRowColor = "#F4F6F0";

    /// <summary>What the Campo palette drew before ME3-B: the accent derived from <see cref="InitialMenuColor"/>.</summary>
    public const string InitialMenuAccentColor = "#D3D7D5";

    /// <summary>WCAG AA for normal text: the accent paints the current item's name.</summary>
    public const double MinimumMenuAccentContrast = 4.5;

    public static bool IsValidColor(string? color) => color is { Length: ColorLength }
        && color[0] == '#' && color.AsSpan(1).IndexOfAnyExcept(HexDigits) < 0;

    public bool IsValid => IsValidColor(PrimaryColor) && IsValidColor(MenuColor)
        && IsValidColor(HeaderColor) && IsValidColor(AlternateRowColor) && HasReadableMenuAccent(MenuAccentColor, MenuColor);

    /// <summary>Both colors well formed and the accent at least <see cref="MinimumMenuAccentContrast"/> against the menu.</summary>
    public static bool HasReadableMenuAccent(string? accent, string? menu) =>
        IsValidColor(accent) && IsValidColor(menu) && ContrastRatio(accent!, menu!) >= MinimumMenuAccentContrast;

    /// <summary>The WCAG 2 contrast ratio of two #RRGGBB colors, from 1 to 21.</summary>
    public static double ContrastRatio(string first, string second)
    {
        var (a, b) = (Luminance(first), Luminance(second));
        return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
    }

    /// <summary>
    /// The accent of an installation that never saved one: the menu mixed 80 % toward its ink (white or black) and then
    /// toward the ink until it reads as text, like the shared UI derived it (with 3:1) before ME3-B.
    /// </summary>
    public static string DeriveMenuAccent(string menu)
    {
        var ink = Ink(menu);
        var start = Mix(menu, ink, .8);
        for (var step = 0; step <= 100; step++)
        {
            var candidate = Mix(start, ink, step / 100d);
            if (ContrastRatio(candidate, menu) >= MinimumMenuAccentContrast) return candidate;
        }

        return ink;
    }

    private static string Ink(string background) =>
        ContrastRatio("#FFFFFF", background) >= ContrastRatio("#000000", background) ? "#FFFFFF" : "#000000";

    private static string Mix(string color, string target, double amount)
    {
        var (from, to) = (Channels(color), Channels(target));
        // Math.round of the shared UI: halves go up.
        return "#" + string.Concat(from.Select((channel, index) =>
            ((int)Math.Round(channel + (to[index] - channel) * amount, MidpointRounding.AwayFromZero)).ToString("X2", CultureInfo.InvariantCulture)));
    }

    private static double Luminance(string color)
    {
        var linear = Channels(color).Select(channel => channel / 255d)
            .Select(value => value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4)).ToArray();
        return linear[0] * .2126 + linear[1] * .7152 + linear[2] * .0722;
    }

    private static int[] Channels(string color) =>
        [Convert.ToInt32(color[1..3], 16), Convert.ToInt32(color[3..5], 16), Convert.ToInt32(color[5..7], 16)];
}
