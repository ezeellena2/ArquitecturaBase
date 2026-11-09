namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// El desglose del IVA de una factura A o B, como lo pide <c>FECAESolicitar</c>: <c>ImpNeto</c> (<see cref="NetTaxed"/>),
/// <c>ImpIVA</c> (<see cref="Vat"/>), <c>ImpOpEx</c> (<see cref="Exempt"/>), <c>ImpTotConc</c> (<see cref="NotTaxed"/>),
/// <c>ImpTotal</c> (<see cref="Total"/>, sin tributos) y el array <c>Iva</c> (<see cref="Rates"/>).
/// </summary>
/// <remarks>
/// Una entrada por alícuota, ordenada por código (10022). El IVA se calcula sobre la suma de los renglones de cada alícuota,
/// no renglón por renglón, con el redondeo half-even de WSFEv1. Con precios con IVA, la base de cada alícuota es su suma
/// dividida por (1 + alícuota), redondeada, y el IVA es lo que falta: el total no cambia y el error contra base × alícuota
/// queda debajo de los 0,01 de la 10051. Una alícuota que suma 0 no va (10020).
/// </remarks>
public sealed record ArcaVatBreakdown(
    decimal NetTaxed,
    decimal Vat,
    decimal Exempt,
    decimal NotTaxed,
    IReadOnlyList<ArcaVatRateTotal> Rates)
{
    public decimal Total => NetTaxed + Vat + Exempt + NotTaxed;

    public static ArcaVatBreakdown Calculate(IEnumerable<ArcaVatLine> lines, bool amountsIncludeVat)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var taxed = new SortedDictionary<int, (ArcaVatRate Rate, decimal Amount)>();
        var exempt = 0m;
        var notTaxed = 0m;
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line.Treatment);
            ArgumentOutOfRangeException.ThrowIfNegative(line.Amount);
            if (Round(line.Amount) != line.Amount)
                throw new ArgumentException("A line amount must have at most two decimals.", nameof(lines));

            switch (line.Treatment.Kind)
            {
                case ArcaVatTreatmentKind.Exempt:
                    exempt += line.Amount;
                    break;
                case ArcaVatTreatmentKind.NotTaxed:
                    notTaxed += line.Amount;
                    break;
                default:
                    var rate = line.Treatment.Rate!;
                    if (taxed.TryGetValue(rate.Id, out var group))
                    {
                        if (group.Rate.Percent != rate.Percent)
                            throw new ArgumentException($"VAT rate {rate.Id} comes with two percents.", nameof(lines));
                        taxed[rate.Id] = (rate, group.Amount + line.Amount);
                    }
                    else
                    {
                        taxed[rate.Id] = (rate, line.Amount);
                    }

                    break;
            }
        }

        var rates = taxed.Values
            .Where(group => group.Amount > 0m)
            .Select(group => Split(group.Rate, group.Amount, amountsIncludeVat))
            .ToArray();

        return new ArcaVatBreakdown(rates.Sum(r => r.Base), rates.Sum(r => r.Vat), exempt, notTaxed, rates);
    }

    private static ArcaVatRateTotal Split(ArcaVatRate rate, decimal amount, bool amountIncludesVat)
    {
        var factor = rate.Percent / 100m;
        if (!amountIncludesVat) return new ArcaVatRateTotal(rate.Id, amount, Round(amount * factor));

        var net = Round(amount / (1m + factor));
        return new ArcaVatRateTotal(rate.Id, net, amount - net);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.ToEven);
}
