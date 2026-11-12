using ArquitecturaBase.Application.Modules.BusinessForms.Services;
namespace ArquitecturaBase.Application.UnitTests.Modules.BusinessForms;
// DOC7-A: the periods of the dashboard in the zone of its buckets (Buenos Aires, UTC-3 without daylight saving time, unless a test
// needs one with it).
public sealed class FormDashboardPeriodPolicyTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");
    private static readonly TimeZoneInfo Madrid = TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");

    [Fact]
    public void October_compares_with_september_because_whole_months_go_back_by_months()
    {
        Assert.Equal((Utc(2026, 9, 1, 3), Utc(2026, 10, 1, 3)), FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 1, 3), Utc(2026, 11, 1, 3), Zone));
        Assert.Equal((Utc(2026, 7, 1, 3), Utc(2026, 9, 1, 3)), FormDashboardPeriodPolicy.Previous(Utc(2026, 9, 1, 3), Utc(2026, 11, 1, 3), Zone));
        var previous = FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 1, 3), Utc(2026, 11, 1, 3), Zone)!.Value;
        Assert.Equal((DateTimeKind.Utc, DateTimeKind.Utc), (previous.FromUtc.Kind, previous.ToUtc.Kind));
    }

    [Fact]
    public void Whole_local_days_go_back_the_same_number_of_days()
    {
        Assert.Equal((Utc(2026, 9, 28, 3), Utc(2026, 10, 5, 3)), FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 5, 3), Utc(2026, 10, 12, 3), Zone));
        // From the 1st to the 15th is not a whole month: fourteen days back.
        Assert.Equal((Utc(2026, 9, 17, 3), Utc(2026, 10, 1, 3)), FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 1, 3), Utc(2026, 10, 15, 3), Zone));
    }

    [Fact]
    public void Across_a_change_of_daylight_saving_time_the_previous_range_starts_at_a_local_midnight()
    {
        // The week after the change in Madrid has 167 hours; the one before starts at 00:00 of Monday the 23rd, 23:00 UTC.
        Assert.Equal((Utc(2026, 3, 22, 23), Utc(2026, 3, 29, 22)), FormDashboardPeriodPolicy.Previous(Utc(2026, 3, 29, 22), Utc(2026, 4, 5, 22), Madrid));
        Assert.Equal((Utc(2026, 10, 18, 22), Utc(2026, 10, 25, 23)), FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 25, 23), Utc(2026, 11, 1, 23), Madrid));
        // October in Madrid ends after the change and still compares with September.
        Assert.Equal((Utc(2026, 8, 31, 22), Utc(2026, 9, 30, 22)), FormDashboardPeriodPolicy.Previous(Utc(2026, 9, 30, 22), Utc(2026, 10, 31, 23), Madrid));
    }

    [Fact]
    public void A_local_midnight_that_does_not_exist_starts_its_day_at_the_first_instant_that_does()
    {
        // In Santiago the clocks go from 00:00 to 01:00 on September 6, 2026: that day starts at 01:00, 04:00 UTC.
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");

        Assert.Equal((Utc(2026, 9, 6, 4), Utc(2026, 9, 7, 3)), FormDashboardPeriodPolicy.Previous(Utc(2026, 9, 7, 3), Utc(2026, 9, 8, 3), santiago));
    }

    [Fact]
    public void Other_instants_keep_the_same_length_and_some_ranges_have_no_previous_one()
    {
        Assert.Equal((Utc(2026, 9, 30, 15, 30), Utc(2026, 10, 1, 15, 30)), FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 1, 15, 30), Utc(2026, 10, 2, 15, 30), Zone));
        Assert.Null(FormDashboardPeriodPolicy.Previous(Utc(2026, 10, 1, 3), null, Zone));
        Assert.Null(FormDashboardPeriodPolicy.Previous(null, Utc(2026, 11, 1, 3), Zone));
        Assert.Null(FormDashboardPeriodPolicy.Previous(DateTime.SpecifyKind(DateTime.MinValue.AddDays(1), DateTimeKind.Utc),
            DateTime.SpecifyKind(DateTime.MinValue.AddDays(10), DateTimeKind.Utc), TimeZoneInfo.Utc));
        Assert.Null(FormDashboardPeriodPolicy.Previous(Utc(1, 3, 1, 0), Utc(1, 6, 1, 0), TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData("2026-11-09", "2026-11-09")]
    [InlineData("2026-11-11", "2026-11-09")]
    [InlineData("2026-11-15", "2026-11-09")]
    [InlineData("2026-11-16", "2026-11-16")]
    [InlineData("2027-01-01", "2026-12-28")]
    public void A_week_starts_on_monday(string day, string monday) =>
        Assert.Equal(DateOnly.Parse(monday, System.Globalization.CultureInfo.InvariantCulture),
            FormDashboardPeriodPolicy.Start(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), "week"));

    [Fact]
    public void A_month_starts_on_its_first_day_and_a_day_is_itself()
    {
        Assert.Equal(new DateOnly(2028, 2, 1), FormDashboardPeriodPolicy.Start(new DateOnly(2028, 2, 29), "month"));
        Assert.Equal(new DateOnly(2028, 2, 29), FormDashboardPeriodPolicy.Start(new DateOnly(2028, 2, 29), "day"));
    }

    [Fact]
    public void A_response_at_half_past_eleven_at_night_is_in_its_local_day()
    {
        // 02:30 UTC is 23:30 of the day before in Buenos Aires.
        Assert.Equal(new DateOnly(2026, 10, 14), FormDashboardPeriodPolicy.LocalDay(Utc(2026, 10, 15, 2, 30), Zone));
        Assert.Equal(new DateOnly(2026, 10, 15), FormDashboardPeriodPolicy.LocalDay(Utc(2026, 10, 15, 3), Zone));
    }

    [Fact]
    public void The_series_of_a_month_in_course_goes_from_its_first_day_to_today()
    {
        var window = FormDashboardPeriodPolicy.Window("day", Zone, Utc(2026, 10, 1, 3), Utc(2026, 11, 1, 3),
            Utc(2026, 10, 3, 12), Utc(2026, 10, 20, 12), Utc(2026, 10, 22, 15));

        Assert.Equal(22, window.Starts.Count);
        Assert.Equal(new DateOnly(2026, 10, 1), window.Starts[0]);
        Assert.Equal(new DateOnly(2026, 10, 22), window.Starts[^1]);
        Assert.False(window.Truncated);
    }

    [Fact]
    public void A_closed_range_shows_all_its_days_even_without_responses()
    {
        var window = FormDashboardPeriodPolicy.Window("day", Zone, Utc(2026, 9, 1, 3), Utc(2026, 10, 1, 3), null, null, Utc(2026, 11, 11, 12));

        Assert.Equal(30, window.Starts.Count);
        Assert.Equal(new DateOnly(2026, 9, 30), window.Starts[^1]);
    }

    [Fact]
    public void Without_a_range_the_series_goes_from_the_first_response_to_today()
    {
        var window = FormDashboardPeriodPolicy.Window("day", Zone, null, null, Utc(2026, 10, 20, 12), Utc(2026, 10, 21, 12), Utc(2026, 10, 23, 12));

        Assert.Equal([new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 21), new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 23)], window.Starts);
    }

    [Fact]
    public void Without_a_range_or_responses_there_is_no_series()
    {
        Assert.Empty(FormDashboardPeriodPolicy.Window("week", Zone, null, null, null, null, Utc(2026, 10, 23, 12)).Starts);
        Assert.Empty(FormDashboardPeriodPolicy.Window("day", Zone, Utc(2026, 12, 1, 3), null, null, null, Utc(2026, 10, 23, 12)).Starts);
    }

    [Fact]
    public void A_response_received_after_now_still_has_its_bucket()
    {
        var window = FormDashboardPeriodPolicy.Window("day", Zone, null, null, Utc(2026, 10, 20, 12), Utc(2026, 10, 24, 12), Utc(2026, 10, 22, 12));

        Assert.Equal(new DateOnly(2026, 10, 24), window.Starts[^1]);
    }

    [Fact]
    public void Weeks_go_from_monday_to_monday()
    {
        var window = FormDashboardPeriodPolicy.Window("week", Zone, Utc(2026, 10, 1, 3), Utc(2026, 11, 1, 3), null, null, Utc(2026, 11, 11, 12));

        Assert.Equal([new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 26)],
            window.Starts);
    }

    [Fact]
    public void Months_keep_their_length_and_february_its_leap_day()
    {
        var months = FormDashboardPeriodPolicy.Window("month", Zone, Utc(2028, 1, 31, 3), Utc(2028, 3, 2, 3), null, null, Utc(2028, 6, 1, 12));
        var days = FormDashboardPeriodPolicy.Window("day", Zone, Utc(2028, 2, 27, 3), Utc(2028, 3, 2, 3), null, null, Utc(2028, 6, 1, 12));

        Assert.Equal([new DateOnly(2028, 1, 1), new DateOnly(2028, 2, 1), new DateOnly(2028, 3, 1)], months.Starts);
        Assert.Equal([new DateOnly(2028, 2, 27), new DateOnly(2028, 2, 28), new DateOnly(2028, 2, 29), new DateOnly(2028, 3, 1)], days.Starts);
    }

    [Fact]
    public void A_series_longer_than_its_cap_keeps_its_latest_buckets_and_says_so()
    {
        var window = FormDashboardPeriodPolicy.Window("day", Zone, Utc(2025, 1, 1, 3), Utc(2027, 1, 1, 3), null, null, Utc(2027, 6, 1, 12));

        Assert.True(window.Truncated);
        Assert.Equal(FormDashboardPeriodPolicy.MaximumBuckets, window.Starts.Count);
        Assert.Equal(new DateOnly(2026, 12, 31), window.Starts[^1]);
        Assert.Equal(new DateOnly(2026, 12, 31).AddDays(-(FormDashboardPeriodPolicy.MaximumBuckets - 1)), window.Starts[0]);
        Assert.False(FormDashboardPeriodPolicy.Window("month", Zone, Utc(2025, 1, 1, 3), Utc(2027, 1, 1, 3), null, null, Utc(2027, 6, 1, 12)).Truncated);
    }

    [Fact]
    public void A_date_question_goes_from_its_first_to_its_last_answered_date_with_the_same_cap()
    {
        var weeks = FormDashboardPeriodPolicy.DateWindow(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 20), "week");
        var days = FormDashboardPeriodPolicy.DateWindow(new DateOnly(1990, 1, 1), new DateOnly(2026, 10, 20), "day");

        Assert.Equal([new DateOnly(2026, 9, 28), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 19)], weeks.Starts);
        Assert.False(weeks.Truncated);
        Assert.True(days.Truncated);
        Assert.Equal((FormDashboardPeriodPolicy.MaximumBuckets, new DateOnly(2026, 10, 20)), (days.Starts.Count, days.Starts[^1]));
    }

    private static DateTime Utc(int year, int month, int day, int hour, int minute = 0) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);
}
