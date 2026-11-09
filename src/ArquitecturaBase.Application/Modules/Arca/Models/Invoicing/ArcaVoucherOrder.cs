namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que se pide emitir: el receptor, el concepto (1 productos, 2 servicios, 3 ambos), la fecha, el período y el vencimiento
/// del pago si es un servicio, y los renglones. El total esperado es el que calculó quien pide; el servidor lo recalcula y
/// no encola si no coincide. La clave del pedido hace la emisión idempotente.
/// </summary>
public sealed record ArcaVoucherOrder(
    Guid RequestKey,
    ArcaVoucherParty Receiver,
    int Concept,
    DateOnly IssueOn,
    DateOnly? ServiceFrom,
    DateOnly? ServiceTo,
    DateOnly? PaymentDueOn,
    IReadOnlyList<ArcaVoucherOrderItem> Items,
    decimal? ExpectedTotal)
{
    public override string ToString() => $"ArcaVoucherOrder {{ requestKey: {RequestKey}, items: {Items.Count}, receiver: [REDACTED] }}";
}
