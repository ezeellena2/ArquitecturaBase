namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Un comprobante tal como va en <c>FECAESolicitar</c>: uno por pedido, con <c>CbteDesde</c> y <c>CbteHasta</c> iguales a
/// <paramref name="Number"/>. Los códigos son los de las tablas de WSFEv1 (tipo, concepto, documento, moneda y condición
/// frente al IVA del receptor). El período y el vencimiento del pago van sólo con concepto 2 o 3.
/// </summary>
public sealed record ArcaVoucherRequest(
    int PointOfSale,
    int VoucherType,
    long Number,
    int Concept,
    int DocumentType,
    long DocumentNumber,
    DateOnly IssuedOn,
    ArcaVoucherAmounts Amounts,
    DateOnly? ServiceFrom,
    DateOnly? ServiceTo,
    DateOnly? PaymentDueOn,
    string CurrencyId,
    decimal CurrencyRate,
    int ReceiverVatConditionId,
    IReadOnlyList<ArcaAssociatedVoucher> Associated)
{
    public override string ToString() => $"ArcaVoucherRequest {{ pointOfSale: {PointOfSale}, type: {VoucherType}, number: {Number}, document: [REDACTED] }}";
}

/// <summary>
/// Los importes del comprobante, con dos decimales: <c>ImpTotal</c> = <c>ImpTotConc</c> + <c>ImpOpEx</c> + <c>ImpNeto</c> +
/// <c>ImpIVA</c> + <c>ImpTrib</c>. En la clase C sólo hay neto (el subtotal) y total, sin alícuotas.
/// </summary>
public sealed record ArcaVoucherAmounts(decimal Total, decimal NotTaxed, decimal Net, decimal Exempt, decimal Taxes, decimal Vat,
    IReadOnlyList<ArcaVatAmount> VatRates);

/// <summary>Una alícuota de IVA (<c>AlicIva</c>): el código de <c>FEParamGetTiposIva</c>, la base y el importe.</summary>
public sealed record ArcaVatAmount(int RateId, decimal Base, decimal Amount);

/// <summary>Un comprobante asociado (<c>CbteAsoc</c>), como la factura que anula una nota de crédito.</summary>
public sealed record ArcaAssociatedVoucher(int VoucherType, int PointOfSale, long Number, string? Cuit, DateOnly? IssuedOn);
