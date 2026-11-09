using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaInvoiceLineTests
{
    [Theory]
    [InlineData("10", "52000", "520000")]
    [InlineData("1.5", "10.01", "15.02")]
    [InlineData("1", "0.125", "0.12")]
    [InlineData("1", "0.135", "0.14")]
    [InlineData("3", "0.005", "0.02")]
    public void A_line_amount_is_quantity_by_price_rounded_half_even_to_two_decimals(string quantity, string price, string amount)
    {
        Assert.Equal(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            ArcaInvoiceLine.Amount(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void The_class_c_total_is_the_sum_of_the_rounded_lines()
    {
        Assert.Equal(15.02m + 0.12m, ArcaInvoiceLine.Total([new ArcaInvoiceLine(1.5m, 10.01m), new ArcaInvoiceLine(1m, 0.125m)]));
    }

    [Theory]
    [InlineData("0", "10")]
    [InlineData("-1", "10")]
    [InlineData("1", "-0.01")]
    public void A_line_without_a_positive_quantity_or_with_a_negative_price_is_a_system_error(string quantity, string price)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ArcaInvoiceLine.Amount(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(price, System.Globalization.CultureInfo.InvariantCulture)));
    }
}
