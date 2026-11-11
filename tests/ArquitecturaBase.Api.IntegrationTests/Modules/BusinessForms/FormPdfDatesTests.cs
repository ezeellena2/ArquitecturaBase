using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPdfDatesTests
{
    [Theory]
    [InlineData("es", "03/11/2026")]
    [InlineData("en", "11/03/2026")]
    public void A_day_is_written_with_numbers_only_in_the_order_of_the_language(string culture, string expected) =>
        Assert.Equal(expected, FormPdfDates.Day(new DateOnly(2026, 11, 3), culture));

    // The time goes after a comma, as the app and the Excel write it: 24 hours in Spanish, AM or PM in English.
    [Theory]
    [InlineData("es", 21, "03/11/2026, 21:05")]
    [InlineData("es", 9, "03/11/2026, 9:05")]
    [InlineData("en", 21, "11/03/2026, 9:05 PM")]
    [InlineData("en", 9, "11/03/2026, 9:05 AM")]
    public void A_moment_adds_the_time_after_a_comma_as_the_app_writes_it(string culture, int hour, string expected) =>
        Assert.Equal(expected, FormPdfDates.Moment(new DateTime(2026, 11, 3, hour, 5, 0, DateTimeKind.Utc), culture));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void The_month_is_never_written_as_a_word(string culture)
    {
        Assert.DoesNotContain(FormPdfDates.Day(new DateOnly(2026, 11, 3), culture), char.IsLetter);
        // In English the time carries AM or PM; the day before the comma, only numbers.
        Assert.DoesNotContain(FormPdfDates.Moment(new DateTime(2026, 11, 3, 9, 5, 0, DateTimeKind.Utc), culture).Split(", ")[0], char.IsLetter);
    }
}
