using ArquitecturaBase.Domain.Modules.Arca.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

/// <summary>
/// Una emisión de la cola como se le responde a quien la pidió: su estado, su número, el CAE, el PDF fiscal y el receptor que
/// fijó al encolarse (<see cref="Receiver"/>).
/// </summary>
public sealed record ArcaVoucherResponse(
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
    long? PdfSize)
{
    /// <summary>El receptor del pedido guardado; lo muestra el resultado de la emisión, como el PDF fiscal.</summary>
    public ArcaVoucherParty? Receiver { get; init; }

    /// <summary>Los códigos y textos de ARCA del último intento (errores antes que observaciones); null si no dijo nada.</summary>
    public IReadOnlyList<ArcaMessage>? Messages { get; init; }

    /// <summary>Las veces seguidas que no salió y quedó esperando (sin conexión, sin respuesta o incierto): las que ya probó.</summary>
    public int Failures { get; init; }
}
