using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Lo que saldría si se emite ahora: la clase y el código que decide el sistema y el total recalculado.</summary>
public sealed record ArcaVoucherPreviewResponse(ArcaVoucherClass Class, int VoucherType, decimal Total);
