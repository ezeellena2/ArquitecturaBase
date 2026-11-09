namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que muestra el PDF fiscal, ya armado en palabras: el emisor, el comprobante («C», «COD. 11», punto de venta y número),
/// el receptor, el período y el vencimiento del pago si es un servicio, los renglones, el total, el CAE y su vencimiento, el
/// QR y, en una nota de crédito, qué factura anula.
/// </summary>
public sealed record ArcaFiscalDocument(
    string IssuerName,
    string IssuerAddress,
    string IssuerTaxStatus,
    string IssuerCuit,
    string? GrossIncomeNumber,
    DateOnly? ActivityStartOn,
    string ClassLetter,
    string TypeCode,
    string Title,
    string Number,
    DateOnly IssuedOn,
    string ReceiverName,
    string ReceiverDocument,
    string ReceiverVatCondition,
    string? ReceiverAddress,
    DateOnly? ServiceFrom,
    DateOnly? ServiceTo,
    DateOnly? PaymentDueOn,
    IReadOnlyList<ArcaVoucherLine> Lines,
    decimal Total,
    string Cae,
    DateOnly CaeDueOn,
    string QrUrl,
    string? Cancels)
{
    public override string ToString() => $"ArcaFiscalDocument {{ number: {Number}, content: [REDACTED] }}";
}
