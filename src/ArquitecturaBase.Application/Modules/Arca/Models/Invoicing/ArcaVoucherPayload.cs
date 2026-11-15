namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que la emisión fija al encolarse, en <c>ArcaVoucher.PayloadJson</c>, ya armado para su clase (H2): el receptor, el período y
/// el vencimiento del pago, los renglones con su importe (y su IVA en la A y la B), los importes del pedido (con el IVA de cada
/// alícuota en la A y la B), los datos del emisor de ese momento (para el PDF fiscal), en una nota de crédito la factura que
/// anula y, en la A y la B, si los precios incluyen el IVA. El worker sólo le pone el número, y la reconciliación y el PDF usan
/// estos importes. <see cref="Version"/> es su forma: una versión de la app lee sólo la suya, de forma estricta, y deja
/// esperando lo que encoló otra.
/// </summary>
public sealed record ArcaVoucherPayload(
    int Version,
    ArcaVoucherParty Receiver,
    DateOnly? ServiceFrom,
    DateOnly? ServiceTo,
    DateOnly? PaymentDueOn,
    IReadOnlyList<ArcaVoucherLine> Lines,
    ArcaVoucherAmounts Amounts,
    ArcaVoucherIssuer Issuer,
    ArcaAssociatedVoucher? Cancels,
    bool PricesIncludeVat)
{
    /// <summary>La forma que escribe y lee esta versión de la app.</summary>
    public const int CurrentVersion = 1;
}
