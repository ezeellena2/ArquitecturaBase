namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>
/// Lo que se elige en la alícuota de un renglón de la factura A o B: una alícuota de ARCA, «Exento» o «No gravado». Sólo el
/// gravado tiene alícuota.
/// </summary>
public sealed record ArcaVatTreatment
{
    private ArcaVatTreatment(ArcaVatTreatmentKind kind, ArcaVatRate? rate)
    {
        Kind = kind;
        Rate = rate;
    }

    public static ArcaVatTreatment Exempt { get; } = new(ArcaVatTreatmentKind.Exempt, null);
    public static ArcaVatTreatment NotTaxed { get; } = new(ArcaVatTreatmentKind.NotTaxed, null);

    public ArcaVatTreatmentKind Kind { get; }
    public ArcaVatRate? Rate { get; }

    public static ArcaVatTreatment Taxed(ArcaVatRate rate)
    {
        ArgumentNullException.ThrowIfNull(rate);
        return new(ArcaVatTreatmentKind.Taxed, rate);
    }
}
