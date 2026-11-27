using ArquitecturaBase.Application.Common.Time;

namespace ArquitecturaBase.Application.UnitTests.Common.Time;

public sealed class BusinessMonthTests
{
    private static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    [Fact]
    public void The_last_night_of_a_month_still_belongs_to_it_in_the_business_zone()
    {
        // 23:00 of the 30th of November in Buenos Aires is already the 1st of December in UTC.
        Assert.Equal(new DateOnly(2026, 11, 1), BusinessMonth.Of(new DateTime(2026, 12, 1, 2, 0, 0, DateTimeKind.Utc), BuenosAires));
    }

    [Fact]
    public void A_month_goes_from_its_local_midnight_to_the_next_one()
    {
        var (startUtc, endUtc) = BusinessMonth.Bounds(new DateOnly(2026, 11, 1), BuenosAires);

        Assert.Equal(new DateTime(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc), startUtc);
        Assert.Equal(new DateTime(2026, 12, 1, 3, 0, 0, DateTimeKind.Utc), endUtc);
    }

    [Theory]
    [InlineData("2026-11", true, 2026, 11)]
    [InlineData("2026-01", true, 2026, 1)]
    [InlineData("2026-13", false, 0, 0)]
    [InlineData("2026-1", false, 0, 0)]
    [InlineData("11/2026", false, 0, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData(null, false, 0, 0)]
    public void A_month_is_read_only_as_year_dash_month(string? value, bool valid, int year, int month)
    {
        Assert.Equal(valid, BusinessMonth.TryParse(value, out var parsed));
        if (valid) Assert.Equal(new DateOnly(year, month, 1), parsed);
    }

    [Fact]
    public void A_month_is_written_back_as_year_dash_month()
    {
        Assert.Equal("2026-03", BusinessMonth.Format(new DateOnly(2026, 3, 1)));
    }
}
