using ArquitecturaBase.Infrastructure.Modules.BusinessForms.Pdf;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.BusinessForms;

public sealed class FormPdfDatesTests
{
    [Theory]
    [InlineData("es", "03/11/2026")]
    [InlineData("en", "11/03/2026")]
    public void A_day_is_written_with_numbers_only_in_the_order_of_the_language(string culture, string expected) =>
        Assert.Equal(expected, FormPdfDates.Day(new DateOnly(2026, 11, 3), culture));

    [Theory]
    [InlineData("es", "03/11/2026 21:05")]
    [InlineData("en", "11/03/2026 21:05")]
    public void A_moment_adds_the_24_hour_clock_after_the_day(string culture, string expected) =>
        Assert.Equal(expected, FormPdfDates.Moment(new DateTime(2026, 11, 3, 21, 5, 0, DateTimeKind.Utc), culture));

    [Theory]
    [InlineData("es")]
    [InlineData("en")]
    public void The_month_is_never_written_as_a_word(string culture)
    {
        Assert.DoesNotContain(FormPdfDates.Day(new DateOnly(2026, 11, 3), culture), char.IsLetter);
        Assert.DoesNotContain(FormPdfDates.Moment(new DateTime(2026, 11, 3, 9, 5, 0, DateTimeKind.Utc), culture), char.IsLetter);
    }
}
