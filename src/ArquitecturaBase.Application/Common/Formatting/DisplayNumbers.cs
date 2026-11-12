using System.Globalization;

namespace ArquitecturaBase.Application.Common.Formatting;

/// <summary>
/// The one number format for people (2026-11-12): the CLDR standard of the region of each language of the app, the same the
/// front writes with Intl: Spanish of Argentina (es-AR, «1.210», «1.234,5») and English of the United States (en-US, «1,210»,
/// «1,234.5»). A text for a person with arguments goes through <see cref="Format"/>, which writes every number with its
/// thousands. Identifiers (codes, CUIT, voucher or document numbers, phones) are not numbers here: they go as text, as they
/// are written. Machine formats (JSON, canonical values, the ARCA web services) keep <see cref="CultureInfo.InvariantCulture"/>.
/// </summary>
public static class DisplayNumbers
{
    private static readonly CultureInfo Spanish = CultureInfo.GetCultureInfo("es-AR");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private const int MaximumScale = 28;
    private const string PercentFormat = "percent";

    /// <summary>The culture of the numbers of a language of the app: en-US for English and es-AR, the default language, for the rest.</summary>
    public static CultureInfo Culture(string culture) => culture.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : Spanish;

    /// <summary>A whole number with its thousands: «1.210», «1,210».</summary>
    public static string Number(long value, string culture) => value.ToString("N0", Culture(culture));

    /// <summary>
    /// A decimal with its thousands and the decimals it has, between <paramref name="minimumDecimals"/> and
    /// <paramref name="maximumDecimals"/> (an amount, 2 and 2: «1.210,00»); without the zeros left over by default.
    /// </summary>
    public static string Number(decimal value, string culture, int minimumDecimals = 0, int maximumDecimals = MaximumScale)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumDecimals);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumDecimals, MaximumScale);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDecimals, minimumDecimals);
        var pattern = maximumDecimals == 0 ? "#,0" : "#,0." + new string('0', minimumDecimals) + new string('#', maximumDecimals - minimumDecimals);
        return value.ToString(pattern, Culture(culture));
    }

    /// <summary>
    /// A percentage, 21 for 21 %, with its sign right after the number in both regions («21%», «10,5%», «10.5%»), as CLDR and
    /// Intl write it in the front; up to <paramref name="maximumDecimals"/> decimals, without the zeros left over. The piece
    /// writes the sign itself: the percent pattern .NET takes from the ICU of Windows still puts a space for es-AR («21 %»).
    /// </summary>
    public static string Percent(decimal value, string culture, int maximumDecimals = 2) =>
        Number(value, culture, 0, maximumDecimals) + Culture(culture).NumberFormat.PercentSymbol;

    /// <summary>
    /// A text for a person with its arguments, in the culture of its language: a number without a format of its own goes with
    /// its thousands (<c>{0}</c> of 1210 is «1.210»), a percentage goes with <c>{0:percent}</c> («21%»), one with a format of its
    /// own keeps it (<c>{0:0.##}</c>), and the rest goes as it is.
    /// </summary>
    public static string Format(string culture, string format, params object?[] arguments) =>
        string.Format(new Formatter(Culture(culture)), format, arguments);

    private sealed class Formatter(CultureInfo culture) : IFormatProvider, ICustomFormatter
    {
        public object? GetFormat(Type? formatType) => formatType == typeof(ICustomFormatter) ? this : culture.GetFormat(formatType);

        public string Format(string? format, object? arg, IFormatProvider? formatProvider)
        {
            if (arg is null) return "";
            if (format == PercentFormat) return Percent(Convert.ToDecimal(arg, CultureInfo.InvariantCulture), culture.Name);
            if (string.IsNullOrEmpty(format))
            {
                switch (arg)
                {
                    case byte or sbyte or short or ushort or int or uint or long:
                        return Convert.ToInt64(arg, CultureInfo.InvariantCulture).ToString("N0", culture);
                    case ulong whole:
                        return whole.ToString("N0", culture);
                    case decimal value:
                        return value.ToString("#,0." + new string('#', MaximumScale), culture);
                    case double or float:
                        return Convert.ToDouble(arg, CultureInfo.InvariantCulture).ToString("#,0.###############", culture);
                }
            }
            return arg is IFormattable formattable ? formattable.ToString(format, culture) : arg.ToString() ?? "";
        }
    }
}
