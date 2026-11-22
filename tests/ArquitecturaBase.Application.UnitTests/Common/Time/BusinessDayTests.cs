using ArquitecturaBase.Application.Common.Time;

namespace ArquitecturaBase.Application.UnitTests.Common.Time;

public sealed class BusinessDayTests
{
    private static readonly TimeZoneInfo BuenosAires = TimeZoneInfo.FindSystemTimeZoneById("America/Argentina/Buenos_Aires");

    [Fact]
    public void At_night_the_business_day_is_still_the_local_one_and_ends_at_its_midnight()
    {
        // 22:30 of the 21st in Buenos Aires is already the 22nd in UTC.
        var day = BusinessDay.Of(new DateTime(2026, 11, 22, 1, 30, 0, DateTimeKind.Utc), BuenosAires);

        Assert.Equal(new DateOnly(2026, 11, 21), day.Day);
        Assert.Equal(new DateTime(2026, 11, 21, 3, 0, 0, DateTimeKind.Utc), day.StartUtc);
        Assert.Equal(new DateTime(2026, 11, 22, 3, 0, 0, DateTimeKind.Utc), day.EndUtc);
    }

    [Fact]
    public void A_local_midnight_that_does_not_exist_starts_at_the_first_valid_minute()
    {
        var santiago = TimeZoneInfo.FindSystemTimeZoneById("America/Santiago");
        Assert.True(santiago.IsInvalidTime(new DateTime(2027, 9, 5)));

        var day = BusinessDay.Of(new DateTime(2027, 9, 5, 12, 0, 0, DateTimeKind.Utc), santiago);

        Assert.Equal(new DateOnly(2027, 9, 5), day.Day);
        Assert.Equal(new DateTime(2027, 9, 5, 4, 0, 0, DateTimeKind.Utc), day.StartUtc);
        Assert.Equal(new DateTime(2027, 9, 6, 3, 0, 0, DateTimeKind.Utc), day.EndUtc);
    }

    [Theory]
    [InlineData("America/Argentina/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("Not/AZone", "UTC")]
    [InlineData("", "UTC")]
    public void The_zone_comes_from_the_settings_or_is_utc_when_it_cannot_be_read(string id, string expected)
    {
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(expected).BaseUtcOffset, BusinessDay.Zone(id).BaseUtcOffset);
    }
}
