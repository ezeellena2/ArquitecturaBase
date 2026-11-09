using ArquitecturaBase.Application.Modules.Arca.Models.Invoicing;

namespace ArquitecturaBase.Application.Modules.Arca.Interfaces.Services.Invoicing;

/// <summary>
/// Los pasos guardados de una emisión (ADR 0028), cada uno en su transacción corta con el lock de la conexión. Los llama el
/// procesador de Infrastructure, que pide a ARCA entre uno y otro, con un scope por paso. Todo paso después de reservar vale
/// sólo con la reserva propia (dueño y fence) y devuelve false o null si otro host la tomó: entonces no se guarda nada.
/// </summary>
public interface IArcaVoucherStore
{
    /// <summary>
    /// Toma la reserva de la secuencia y devuelve su comprobante si le toca: el pendiente, o el más viejo en cola con su espera
    /// cumplida. Si no puede intentarse (sin conexión o con otra, sin certificado, o con la fecha fuera de la ventana) lo deja
    /// esperando o en revisión, suelta la reserva y devuelve null.
    /// </summary>
    Task<ArcaVoucherWorkResponse?> ReserveAsync(ArcaVoucherSequenceRow sequence, Guid owner, TimeSpan lease, CancellationToken cancellationToken);

    /// <summary>Guarda el número (último autorizado más 1) y la huella antes de pedir, y devuelve el trabajo con ese número.</summary>
    Task<ArcaVoucherWorkResponse?> AssignNumberAsync(ArcaVoucherWorkResponse work, long lastAuthorized, CancellationToken cancellationToken);

    /// <summary>
    /// Aplica la reconciliación: si ARCA lo emitió, lo autoriza; si alguien usó el número o falta, lo deja en revisión. Si no
    /// se emitió, lo vuelve a poner en envío y devuelve el trabajo para mandarlo otra vez con el mismo número.
    /// </summary>
    Task<ArcaVoucherWorkResponse?> ReconcileAsync(ArcaVoucherWorkResponse work, ArcaIssuedVoucher? found, long lastAuthorized, CancellationToken cancellationToken);

    /// <summary>Guarda lo que respondió <c>FECAESolicitar</c> y suelta la reserva.</summary>
    Task<bool> CompleteAsync(ArcaVoucherWorkResponse work, ArcaAuthorization authorization, CancellationToken cancellationToken);

    /// <summary>No se pudo intentar o no se sabe qué pasó: espera con su espera creciente, y suelta la reserva.</summary>
    Task<bool> PostponeAsync(ArcaVoucherWorkResponse work, string failureCode, CancellationToken cancellationToken);

    /// <summary>Lo que muestra el PDF fiscal de un comprobante autorizado que todavía no lo tiene; null si ya lo tiene. Sólo lee.</summary>
    Task<ArcaFiscalPdfWorkResponse?> FindPdfAsync(Guid voucherId, CancellationToken cancellationToken);

    /// <summary>Guarda la referencia del PDF ya escrito; si otro host la guardó antes, deja la suya.</summary>
    Task<bool> AttachPdfAsync(Guid voucherId, string storageKey, string sha256, long size, CancellationToken cancellationToken);

    /// <summary>El almacén falló: el comprobante sigue autorizado y el PDF se reintenta con su espera creciente.</summary>
    Task<bool> PostponePdfAsync(Guid voucherId, string failureCode, CancellationToken cancellationToken);
}
