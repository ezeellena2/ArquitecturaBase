namespace ArquitecturaBase.Application.Common.Time;

/// <summary>
/// The business's day in its time zone (Configuración › Zona horaria), as the UTC instants its daily limits count between: the
/// assistant's form offers and replies, the AI's quotas and the re-reads of documents (RC5, 2026-11-22). A local midnight that does
/// not exist (a daylight saving gap) starts at the first valid minute.
/// </summary>
public static class BusinessDay
{
    /// <summary>The zone of the settings by its id, or UTC when it cannot be read.</summary>
    public static TimeZoneInfo Zone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone) ? zone : TimeZoneInfo.Utc;

    public static DateTime LocalDay(DateTimeOffset now, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(now, zone).Date;

    public static DateTime StartUtc(DateTime localDay, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(localDay)) localDay = localDay.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(localDay, zone);
    }

    /// <summary>The business day of <paramref name="utcNow"/>: its local date and the UTC instants it starts and ends.</summary>
    public static (DateOnly Day, DateTime StartUtc, DateTime EndUtc) Of(DateTime utcNow, TimeZoneInfo zone)
    {
        var day = LocalDay(new DateTimeOffset(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)), zone);
        return (DateOnly.FromDateTime(day), StartUtc(day, zone), StartUtc(day.AddDays(1), zone));
    }
}
