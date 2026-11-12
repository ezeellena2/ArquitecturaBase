namespace ArquitecturaBase.Application.Modules.WhatsApp.Services;

/// <summary>
/// The business's day in its time zone, as the UTC instant the daily limits of the assistant count from (the two form offers and
/// the replies of the cases). A local midnight that does not exist (a daylight saving gap) starts at the first valid minute.
/// </summary>
internal static class WhatsAppBusinessDay
{
    public static DateTime LocalDay(DateTimeOffset now, TimeZoneInfo zone) => TimeZoneInfo.ConvertTime(now, zone).Date;

    public static DateTime StartUtc(DateTime localDay, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(localDay)) localDay = localDay.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(localDay, zone);
    }
}
