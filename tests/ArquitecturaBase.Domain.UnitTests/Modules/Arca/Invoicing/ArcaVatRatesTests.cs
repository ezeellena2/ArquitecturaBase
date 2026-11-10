using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

public sealed class ArcaVatRatesTests
{
    [Theory]
    [InlineData(3, "0")]
    [InlineData(4, "10.5")]
    [InlineData(5, "21")]
    [InlineData(6, "27")]
    [InlineData(8, "5")]
    [InlineData(9, "2.5")]
    public void Each_rate_of_arca_has_its_percent(int id, string percent)
    {
        Assert.Equal(new ArcaVatRate(id, decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture)), ArcaVatRates.Find(id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    public void A_code_that_arca_does_not_have_is_not_a_rate(int id)
    {
        Assert.Null(ArcaVatRates.Find(id));
    }
}
