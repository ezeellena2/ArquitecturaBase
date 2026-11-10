namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que la emisión fija al encolarse, en <c>ArcaVoucher.PayloadJson</c>: el receptor, el período y el vencimiento del pago,
/// los renglones con su importe (y su IVA en la A y la B), los datos del emisor de ese momento (para el PDF fiscal), en una
/// nota de crédito la factura que anula y, en la A y la B, si los precios incluyen el IVA.
/// </summary>
public sealed record ArcaVoucherPayload(
    ArcaVoucherParty Receiver,
    DateOnly? ServiceFrom,
    DateOnly? ServiceTo,
    DateOnly? PaymentDueOn,
    IReadOnlyList<ArcaVoucherLine> Lines,
    ArcaVoucherIssuer Issuer,
    ArcaAssociatedVoucher? Cancels,
    bool PricesIncludeVat = false);
