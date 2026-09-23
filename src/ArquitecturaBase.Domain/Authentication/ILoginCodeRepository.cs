namespace ArquitecturaBase.Domain.Authentication;

/// <summary>
/// Los códigos se buscan por destino y propósito, pero el lock y los límites son por destino, compartidos entre
/// propósitos: protegen a quien recibe los mensajes, sea cual sea el motivo (sección 6.3 del spec del ingreso con
/// WhatsApp).
/// </summary>
public interface ILoginCodeRepository
{
    /// <summary>
    /// Pone en fila los pedidos y las verificaciones de códigos de un mismo destino, con cualquier propósito, hasta que
    /// termine la unidad de trabajo. Sin esto, dos requests simultáneas leen el mismo estado y se saltean los límites
    /// de la sección 5.3.
    /// </summary>
    Task LockDestinationAsync(LoginCodeDestination destination, CancellationToken cancellationToken);

    /// <summary>
    /// El último código de ese destino y propósito que no fue reemplazado por uno nuevo (puede estar vencido o usado).
    /// </summary>
    Task<LoginCode?> GetLatestAsync(LoginCodeDestination destination, LoginCodePurpose purpose, CancellationToken cancellationToken);

    /// <summary>
    /// Los códigos todavía activos de ese destino y propósito, para invalidarlos cuando se pide uno nuevo con el mismo
    /// propósito.
    /// </summary>
    Task<IReadOnlyList<LoginCode>> ListActiveAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cuándo se pidió cada código de ese destino desde <paramref name="sinceUtc"/>, con cualquier propósito, del más
    /// viejo al más nuevo.
    /// </summary>
    Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(
        LoginCodeDestination destination,
        DateTime sinceUtc,
        CancellationToken cancellationToken);

    void Add(LoginCode loginCode);
}
