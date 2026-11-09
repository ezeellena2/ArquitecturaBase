namespace ArquitecturaBase.Domain.Modules.Arca.Invoicing;

/// <summary>Cómo trata el IVA a un renglón: gravado a una alícuota, exento (<c>ImpOpEx</c>) o no gravado (<c>ImpTotConc</c>).</summary>
public enum ArcaVatTreatmentKind
{
    Taxed,
    Exempt,
    NotTaxed,
}
