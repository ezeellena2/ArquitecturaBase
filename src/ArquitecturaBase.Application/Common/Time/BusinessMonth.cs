using System.Globalization;

namespace ArquitecturaBase.Application.Common.Time;

/// <summary>
/// The business's month in its time zone (Configuración › Zona horaria), the one Consumo shows (PR16, 2026-11-27): it starts at the
/// local midnight of its first day, as <see cref="BusinessDay"/> does. A month travels as <c>yyyy-MM</c> and is held as its first day.
/// </summary>
public static class BusinessMonth
{
    private const string Pattern = "yyyy-MM";

    public static bool TryParse(string? value, out DateOnly month)
    {
        var valid = DateOnly.TryParseExact(value, Pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed);
        month = valid ? new DateOnly(parsed.Year, parsed.Month, 1) : default;
        return valid;
    }

    public static string Format(DateOnly month) => month.ToString(Pattern, CultureInfo.InvariantCulture);

    /// <summary>The month an instant belongs to in the business zone, as its first day.</summary>
    public static DateOnly Of(DateTime utc, TimeZoneInfo zone)
    {
        var day = BusinessDay.LocalDay(new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)), zone);
        return new DateOnly(day.Year, day.Month, 1);
    }

    /// <summary>The UTC instants a month starts and ends between.</summary>
    public static (DateTime StartUtc, DateTime EndUtc) Bounds(DateOnly month, TimeZoneInfo zone) =>
        (BusinessDay.StartUtc(month.ToDateTime(TimeOnly.MinValue), zone), BusinessDay.StartUtc(month.AddMonths(1).ToDateTime(TimeOnly.MinValue), zone));
}
