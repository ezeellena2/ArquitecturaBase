using ArquitecturaBase.Application.Modules.Arca.Models.Connection;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que un host reservó para trabajar: el comprobante, la reserva de su secuencia (dueño y fence), el certificado con el que
/// se pide el ticket y el pedido armado. Sin número todavía, <c>Request.Number</c> es 0.
/// </summary>
public sealed record ArcaVoucherWorkResponse(Guid VoucherId, Guid Owner, long Fence, ArcaVoucherSequenceRow Sequence, ArcaCertificateRef Certificate,
    ArcaVoucherRequest Request, bool HasNumber);
