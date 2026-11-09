using System.Globalization;
using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Domain.UnitTests.Modules.Arca.Invoicing;

/// <summary>
/// El desglose del IVA de una factura A o B (etapa 4): los importes de `FECAESolicitar` y el array `Iva`, con las
/// validaciones 10022, 10023, 10048, 10051 y 10061 del manual de WSFEv1.
/// </summary>
public sealed class ArcaVatBreakdownTests
{
    private static readonly ArcaVatRate Zero = new(3, 0m);
    private static readonly ArcaVatRate TenAndAHalf = new(4, 10.5m);
    private static readonly ArcaVatRate TwentyOne = new(5, 21m);
    private static readonly ArcaVatRate TwentySeven = new(6, 27m);
    private static readonly ArcaVatRate Five = new(8, 5m);
    private static readonly ArcaVatRate TwoAndAHalf = new(9, 2.5m);

    private static ArcaVatLine Taxed(decimal amount, ArcaVatRate rate) => new(amount, ArcaVatTreatment.Taxed(rate));

    [Fact]
    public void Net_prices_add_the_vat_of_their_rate()
    {
        var breakdown = ArcaVatBreakdown.Calculate([Taxed(100m, TwentyOne)], amountsIncludeVat: false);

        Assert.Equal(100m, breakdown.NetTaxed);
        Assert.Equal(21m, breakdown.Vat);
        Assert.Equal(121m, breakdown.Total);
        Assert.Equal([new ArcaVatRateTotal(5, 100m, 21m)], breakdown.Rates);
    }

    [Theory]
    [InlineData("0.50", "0.10")]
    [InlineData("1.50", "0.32")]
    [InlineData("2.50", "0.52")]
    public void The_vat_is_rounded_half_even_like_arca(string net, string vat)
    {
        var breakdown = ArcaVatBreakdown.Calculate([Taxed(decimal.Parse(net, CultureInfo.InvariantCulture), TwentyOne)], false);

        Assert.Equal(decimal.Parse(vat, CultureInfo.InvariantCulture), breakdown.Vat);
    }

    [Fact]
    public void Lines_with_the_same_rate_are_added_before_the_vat()
    {
        // Por renglón darían 0,01 cada uno (0,03); ARCA pide una entrada por alícuota con su IVA (10022 y 10051).
        var breakdown = ArcaVatBreakdown.Calculate(
            [Taxed(0.05m, TenAndAHalf), Taxed(0.05m, TenAndAHalf), Taxed(0.05m, TenAndAHalf)],
            false);

        Assert.Equal([new ArcaVatRateTotal(4, 0.15m, 0.02m)], breakdown.Rates);
        Assert.Equal(0.02m, breakdown.Vat);
    }

    [Fact]
    public void Each_rate_is_one_entry_ordered_by_its_code()
    {
        var breakdown = ArcaVatBreakdown.Calculate(
            [Taxed(200m, TwentyOne), Taxed(100m, TenAndAHalf), Taxed(50m, TwentyOne), Taxed(10m, TwentySeven)],
            false);

        Assert.Equal(
            [new ArcaVatRateTotal(4, 100m, 10.5m), new ArcaVatRateTotal(5, 250m, 52.5m), new ArcaVatRateTotal(6, 10m, 2.7m)],
            breakdown.Rates);
        Assert.Equal(360m, breakdown.NetTaxed);
        Assert.Equal(65.7m, breakdown.Vat);
        Assert.Equal(425.7m, breakdown.Total);
    }

    [Fact]
    public void A_net_at_zero_percent_goes_with_its_rate_and_no_vat()
    {
        // 10070: con neto gravado el array es obligatorio, aunque la alícuota sea 0 %.
        var breakdown = ArcaVatBreakdown.Calculate([Taxed(80m, Zero)], false);

        Assert.Equal([new ArcaVatRateTotal(3, 80m, 0m)], breakdown.Rates);
        Assert.Equal(80m, breakdown.NetTaxed);
        Assert.Equal(0m, breakdown.Vat);
        Assert.Equal(80m, breakdown.Total);
    }

    [Fact]
    public void Exempt_and_not_taxed_go_outside_the_rates()
    {
        var breakdown = ArcaVatBreakdown.Calculate(
            [
                new ArcaVatLine(50m, ArcaVatTreatment.Exempt),
                new ArcaVatLine(20m, ArcaVatTreatment.NotTaxed),
                new ArcaVatLine(5m, ArcaVatTreatment.Exempt),
            ],
            false);

        Assert.Empty(breakdown.Rates);
        Assert.Equal(0m, breakdown.NetTaxed);
        Assert.Equal(0m, breakdown.Vat);
        Assert.Equal(55m, breakdown.Exempt);
        Assert.Equal(20m, breakdown.NotTaxed);
        Assert.Equal(75m, breakdown.Total);
    }

    [Fact]
    public void An_invoice_can_mix_taxed_exempt_and_not_taxed_lines()
    {
        var breakdown = ArcaVatBreakdown.Calculate(
            [Taxed(100m, TwentyOne), new ArcaVatLine(30m, ArcaVatTreatment.Exempt), new ArcaVatLine(12m, ArcaVatTreatment.NotTaxed)],
            false);

        Assert.Equal(100m, breakdown.NetTaxed);
        Assert.Equal(21m, breakdown.Vat);
        Assert.Equal(30m, breakdown.Exempt);
        Assert.Equal(12m, breakdown.NotTaxed);
        Assert.Equal(163m, breakdown.Total);
    }

    [Fact]
    public void Prices_with_vat_split_the_base_and_the_vat_of_each_rate()
    {
        var breakdown = ArcaVatBreakdown.Calculate([Taxed(600m, TwentyOne), Taxed(400m, TwentyOne)], amountsIncludeVat: true);

        // 1000 / 1,21 = 826,446…: la base se redondea y el IVA es lo que falta, así el total queda en 1000.
        Assert.Equal([new ArcaVatRateTotal(5, 826.45m, 173.55m)], breakdown.Rates);
        Assert.Equal(1000m, breakdown.Total);
    }

    [Fact]
    public void Exempt_and_not_taxed_amounts_are_the_same_whether_prices_include_vat_or_not()
    {
        ArcaVatLine[] lines = [new(50m, ArcaVatTreatment.Exempt), new(20m, ArcaVatTreatment.NotTaxed)];

        Assert.Equal(ArcaVatBreakdown.Calculate(lines, false).Total, ArcaVatBreakdown.Calculate(lines, true).Total);
        Assert.Equal(50m, ArcaVatBreakdown.Calculate(lines, true).Exempt);
    }

    [Fact]
    public void Prices_with_vat_keep_their_total_and_stay_within_the_tolerance_of_arca()
    {
        // 10051: el IVA de cada alícuota corresponde a su base con un error de 0,01 como mucho.
        foreach (var rate in new[] { TwoAndAHalf, Five, TenAndAHalf, TwentyOne, TwentySeven })
        {
            for (var cents = 1; cents <= 5000; cents++)
            {
                var gross = cents / 100m;
                var entry = Assert.Single(ArcaVatBreakdown.Calculate([Taxed(gross, rate)], true).Rates);

                Assert.Equal(gross, entry.Base + entry.Vat);
                Assert.True(
                    Math.Abs(entry.Vat - entry.Base * rate.Percent / 100m) <= 0.01m,
                    $"{gross} at {rate.Percent} %: base {entry.Base}, vat {entry.Vat}");
            }
        }
    }

    [Fact]
    public void A_rate_whose_lines_add_up_to_zero_is_left_out()
    {
        // 10020: la base de cada alícuota tiene que ser mayor que 0.
        var breakdown = ArcaVatBreakdown.Calculate([Taxed(0m, TwentyOne), Taxed(10m, TenAndAHalf)], false);

        Assert.Equal([new ArcaVatRateTotal(4, 10m, 1.05m)], breakdown.Rates);
    }

    [Fact]
    public void An_invoice_without_lines_is_all_zero()
    {
        var breakdown = ArcaVatBreakdown.Calculate([], false);

        Assert.Empty(breakdown.Rates);
        Assert.Equal(0m, breakdown.Total);
    }

    [Fact]
    public void An_amount_with_more_than_two_decimals_is_a_bug()
    {
        // El importe del renglón llega redondeado a 2 decimales con half-even (etapa 3).
        Assert.Throws<ArgumentException>(() => ArcaVatBreakdown.Calculate([Taxed(1.005m, TwentyOne)], false));
    }

    [Fact]
    public void A_negative_amount_is_a_bug()
    {
        // La carga los rechaza antes; ARCA no admite importes negativos (10043 a 10047).
        Assert.Throws<ArgumentOutOfRangeException>(() => ArcaVatBreakdown.Calculate([Taxed(-1m, TwentyOne)], false));
    }

    [Fact]
    public void The_same_rate_code_with_another_percent_is_a_bug()
    {
        Assert.Throws<ArgumentException>(
            () => ArcaVatBreakdown.Calculate([Taxed(10m, TwentyOne), Taxed(10m, new ArcaVatRate(5, 10.5m))], false));
    }

    [Theory]
    [InlineData(0, "21")]
    [InlineData(5, "-1")]
    [InlineData(5, "100.01")]
    public void A_rate_needs_a_code_and_a_percent_from_0_to_100(int id, string percent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArcaVatRate(id, decimal.Parse(percent, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void Only_a_taxed_line_has_a_rate()
    {
        Assert.Equal(TwentyOne, ArcaVatTreatment.Taxed(TwentyOne).Rate);
        Assert.Equal(ArcaVatTreatmentKind.Taxed, ArcaVatTreatment.Taxed(TwentyOne).Kind);
        Assert.Null(ArcaVatTreatment.Exempt.Rate);
        Assert.Equal(ArcaVatTreatmentKind.Exempt, ArcaVatTreatment.Exempt.Kind);
        Assert.Null(ArcaVatTreatment.NotTaxed.Rate);
        Assert.Equal(ArcaVatTreatmentKind.NotTaxed, ArcaVatTreatment.NotTaxed.Kind);
    }
}
