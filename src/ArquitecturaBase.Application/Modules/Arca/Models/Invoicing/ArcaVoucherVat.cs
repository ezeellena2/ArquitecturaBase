using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// El IVA de un renglón de la factura A o B: gravado a una alícuota de ARCA (su código de <c>FEParamGetTiposIva</c>), exento o
/// no gravado, sin alícuota. El porcentaje lo pone el servidor (<see cref="ArcaVatRates"/>). La C no lo usa.
/// </summary>
public sealed record ArcaVoucherVat(ArcaVatTreatmentKind Kind, int? RateId = null)
{
    /// <summary>Lo que entiende el desglose, o null si el código no es una alícuota de ARCA o sobra.</summary>
    public ArcaVatTreatment? ToTreatment() => Kind switch
    {
        ArcaVatTreatmentKind.Taxed when RateId is { } id && ArcaVatRates.Find(id) is { } rate => ArcaVatTreatment.Taxed(rate),
        ArcaVatTreatmentKind.Exempt when RateId is null => ArcaVatTreatment.Exempt,
        ArcaVatTreatmentKind.NotTaxed when RateId is null => ArcaVatTreatment.NotTaxed,
        _ => null,
    };
}
