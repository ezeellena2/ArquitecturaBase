using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Lo que saldría si se emite ahora: la clase y el código que decide el sistema, el total recalculado y el IVA (0 en la C).
/// </summary>
public sealed record ArcaVoucherPreviewResponse(ArcaVoucherClass Class, int VoucherType, decimal Total, decimal Vat);
