using ArquitecturaBase.Application.Common.Formatting;

namespace ArquitecturaBase.Application.UnitTests.Common.Formatting;

// The one number format for people (2026-11-12): the CLDR standard of the region of each language of the app, es-AR and
// en-US, the same the front writes with Intl.
public sealed class DisplayNumbersTests
{
    [Theory]
    [InlineData("es", 1210, "1.210")]
    [InlineData("es", 750280, "750.280")]
    [InlineData("es", -1234567, "-1.234.567")]
    [InlineData("en", 1210, "1,210")]
    [InlineData("en", -1234567, "-1,234,567")]
    public void A_whole_number_goes_with_its_thousands_in_the_region_of_its_language(string culture, long value, string expected) =>
        Assert.Equal(expected, DisplayNumbers.Number(value, culture));

    [Theory]
    [InlineData("es", "1234.5", 0, 28, "1.234,5")]
    [InlineData("en", "1234.5", 0, 28, "1,234.5")]
    [InlineData("es", "0.0000000000000000000000000001", 0, 28, "0,0000000000000000000000000001")]
    [InlineData("es", "1210", 2, 2, "1.210,00")]
    [InlineData("en", "1234.567", 2, 2, "1,234.57")]
    [InlineData("es", "1234.5", 2, 28, "1.234,50")]
    public void A_decimal_keeps_its_digits_with_its_thousands_and_the_decimals_asked_for(string culture, string value, int minimum, int maximum, string expected) =>
        Assert.Equal(expected, DisplayNumbers.Number(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), culture, minimum, maximum));

    [Theory]
    [InlineData("es", "es-AR")]
    [InlineData("es-AR", "es-AR")]
    [InlineData("en", "en-US")]
    [InlineData("en-US", "en-US")]
    public void Each_language_of_the_app_has_its_region(string culture, string region) =>
        Assert.Equal(region, DisplayNumbers.Culture(culture).Name);

    [Fact]
    public void A_text_writes_its_numbers_with_their_thousands_and_leaves_the_rest_as_it_is()
    {
        Assert.Equal("1.210 opciones, 1.234,5 en total", DisplayNumbers.Format("es", "{0} opciones, {1} en total", 1210, 1234.5m));
        Assert.Equal("1,210 options, 1,234.5 in total", DisplayNumbers.Format("en", "{0} options, {1} in total", 1210, 1234.5m));
        // An identifier goes as text: as it is written.
        Assert.Equal("Comprobante 0004-00001210", DisplayNumbers.Format("es", "Comprobante {0}", "0004-00001210"));
        Assert.Equal("Sin valor: ", DisplayNumbers.Format("es", "Sin valor: {0}", (object?)null));
    }

    [Fact]
    public void A_number_with_a_format_of_its_own_keeps_it_in_the_region_of_its_language()
    {
        Assert.Equal("Precio 10,50", DisplayNumbers.Format("es", "Precio {0:0.00}", 10.5m));
        Assert.Equal("Price 10.50", DisplayNumbers.Format("en", "Price {0:0.00}", 10.5m));
    }

    // A percentage goes with its sign right after the number in both regions, as Intl writes it in the front (2026-11-12):
    // the percent pattern .NET takes from the ICU of Windows still puts a space for es-AR («21 %»).
    [Theory]
    [InlineData("es", "21", "21%")]
    [InlineData("es", "10.5", "10,5%")]
    [InlineData("es", "2.5", "2,5%")]
    [InlineData("es", "1250", "1.250%")]
    [InlineData("en", "10.5", "10.5%")]
    [InlineData("en", "1250", "1,250%")]
    public void A_percentage_goes_with_its_sign_right_after_the_number(string culture, string value, string expected) =>
        Assert.Equal(expected, DisplayNumbers.Percent(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture), culture));

    [Fact]
    public void A_text_writes_a_percentage_with_the_percent_format()
    {
        Assert.Equal("IVA 10,5%", DisplayNumbers.Format("es", "IVA {0:percent}", 10.5m));
        Assert.Equal("VAT 10.5%", DisplayNumbers.Format("en", "VAT {0:percent}", 10.5m));
    }
}
