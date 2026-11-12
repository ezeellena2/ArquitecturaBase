namespace ArquitecturaBase.Application.Modules.BusinessForms.Services;
/// <summary>
/// The periods of the dashboard (DOC7-A) in the zone of its buckets: the previous range by the calendar of that zone, the bucket a
/// local day falls in (the day, its week from Monday or its month) and the buckets a series shows, at most <see cref="MaximumBuckets"/>.
/// </summary>
internal static class FormDashboardPeriodPolicy
{
    /// <summary>The most buckets a series shows: more than a year of days. A longer one keeps the latest and says so.</summary>
    public const int MaximumBuckets = 400;

    /// <summary>
    /// The range right before [from, to). With both bounds at local midnights it steps back in the calendar of the zone: a range of
    /// whole months (from a 1st to a 1st) goes back the same number of months, so October compares with September, and otherwise
    /// the same number of local days, also across a change of daylight saving time; any other range, the same length. None without
    /// both bounds or before the first instant.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc)? Previous(DateTime? fromUtc, DateTime? toUtc, TimeZoneInfo zone)
    {
        if (fromUtc is not { } from || toUtc is not { } to) return null;
        from = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var localFrom = TimeZoneInfo.ConvertTimeFromUtc(from, zone);
        var localTo = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(to, DateTimeKind.Utc), zone);
        if (localFrom.TimeOfDay == TimeSpan.Zero && localTo.TimeOfDay == TimeSpan.Zero)
        {
            var months = localFrom.Day == 1 && localTo.Day == 1 ? (localTo.Year - localFrom.Year) * 12 + localTo.Month - localFrom.Month : 0;
            var days = (localTo.Date - localFrom.Date).Days;
            var steps = months > 0 ? (localFrom.Year - 1) * 12L + localFrom.Month - 1 - months : (long)DateOnly.FromDateTime(localFrom).DayNumber - days;
            if (steps < 0) return null;
            var start = months > 0 ? localFrom.AddMonths(-months) : localFrom.AddDays(-days);
            return (Midnight(start, zone), from);
        }
        var length = to.Ticks - from.Ticks;
        return from.Ticks - length < DateTime.MinValue.Ticks ? null : (new DateTime(from.Ticks - length, DateTimeKind.Utc), from);
    }

    public static DateOnly Start(DateOnly day, string period) => period switch
    {
        "week" => day.AddDays(-(((int)day.DayOfWeek + 6) % 7)),
        "month" => new DateOnly(day.Year, day.Month, 1),
        _ => day,
    };

    public static DateOnly LocalDay(DateTime utc, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone));

    /// <summary>
    /// The buckets of the received dates: from the start of the range, or the first response, to its end, or now, and always up to
    /// the last response; a month in course ends today. Without a start or with an end before it, none; past the cap, the latest ones.
    /// </summary>
    public static FormDashboardWindow Window(string period, TimeZoneInfo zone, DateTime? fromUtc, DateTime? toUtc,
        DateTime? firstUtc, DateTime? lastUtc, DateTime nowUtc)
    {
        if ((fromUtc ?? firstUtc) is not { } start) return FormDashboardWindow.Empty;
        var end = toUtc is { } to && to.Ticks > DateTime.MinValue.Ticks && to.AddTicks(-1) < nowUtc ? to.AddTicks(-1) : nowUtc;
        if (lastUtc is { } last && last > end) end = last;
        return DateWindow(LocalDay(start, zone), LocalDay(end, zone), period);
    }

    /// <summary>The buckets from the one of a first date to the one of a last date, the latest ones past the cap; none if the last is before.</summary>
    public static FormDashboardWindow DateWindow(DateOnly firstDay, DateOnly lastDay, string period)
    {
        var first = Start(firstDay, period);
        var final = Start(lastDay, period);
        if (final < first) return FormDashboardWindow.Empty;
        var count = period switch
        {
            "week" => (final.DayNumber - first.DayNumber) / 7 + 1,
            "month" => (final.Year - first.Year) * 12 + final.Month - first.Month + 1,
            _ => final.DayNumber - first.DayNumber + 1,
        };
        var truncated = count > MaximumBuckets;
        if (truncated) (first, count) = (Step(final, period, 1 - MaximumBuckets), MaximumBuckets);
        return new(Enumerable.Range(0, count).Select(index => Step(first, period, index)).ToArray(), truncated);
    }

    private static DateOnly Step(DateOnly start, string period, int count) => period switch
    {
        "week" => start.AddDays(7 * count),
        "month" => start.AddMonths(count),
        _ => start.AddDays(count),
    };

    // A local midnight in UTC; where the clocks skip it (daylight saving time starting at 00:00) the day starts when they resume.
    private static DateTime Midnight(DateTime local, TimeZoneInfo zone)
    {
        var day = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(day)) day = day.AddMinutes(15);
        return TimeZoneInfo.ConvertTimeToUtc(day, zone);
    }
}

/// <summary>The dates the buckets of a series start on, in order, and whether older ones were left out.</summary>
internal sealed record FormDashboardWindow(IReadOnlyList<DateOnly> Starts, bool Truncated)
{
    public static readonly FormDashboardWindow Empty = new([], false);
}
