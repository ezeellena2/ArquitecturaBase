using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>Una emisión de la cola tal como se lee, sin seguimiento: como la devuelve el lector.</summary>
public sealed record ArcaVoucherRow(
    Guid Id,
    Guid RequestKey,
    ArcaVoucherState State,
    ArcaVoucherReviewReason? ReviewReason,
    ArcaVoucherClass Class,
    ArcaVoucherKind Kind,
    int VoucherType,
    int PointOfSale,
    long? Number,
    string? Cae,
    DateOnly? CaeDueOn,
    DateOnly? IssuedOn,
    DateOnly RequestedOn,
    decimal Total,
    string? FailureCode,
    bool WaitedForArca,
    DateTime NextAttemptAtUtc,
    int Revision,
    string? PdfStorageKey,
    string? PdfSha256,
    long? PdfSize);
