using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaIssueDateWindowTests
{
    private static readonly DateOnly Today = new(2026, 11, 9);

    [Theory]
    [InlineData(1, -5, true)]
    [InlineData(1, -6, false)]
    [InlineData(1, 5, true)]
    [InlineData(1, 6, false)]
    [InlineData(2, -10, true)]
    [InlineData(2, -11, false)]
    [InlineData(3, 10, true)]
    [InlineData(3, 11, false)]
    public void Products_take_five_days_around_today_and_services_ten(int concept, int days, bool admitted)
    {
        Assert.Equal(admitted, ArcaIssueDateWindow.Admits(concept, Today.AddDays(days), Today));
    }

    [Fact]
    public void A_product_date_cannot_go_into_the_next_month()
    {
        var endOfMonth = new DateOnly(2026, 11, 28);

        Assert.True(ArcaIssueDateWindow.Admits(1, new DateOnly(2026, 11, 30), endOfMonth));
        Assert.False(ArcaIssueDateWindow.Admits(1, new DateOnly(2026, 12, 1), endOfMonth));
        Assert.True(ArcaIssueDateWindow.Admits(2, new DateOnly(2026, 12, 1), endOfMonth));
    }

    [Fact]
    public void A_product_date_can_come_from_the_previous_month()
    {
        Assert.True(ArcaIssueDateWindow.Admits(1, new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 2)));
    }

    [Theory]
    [InlineData("2026-11-09T02:59:00Z", "2026-11-08")]
    [InlineData("2026-11-09T03:00:00Z", "2026-11-09")]
    public void Today_is_the_date_in_argentina(string utc, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            ArcaIssueDateWindow.Today(DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal)));
    }
}
