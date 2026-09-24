using ArquitecturaBase.Domain.Authentication;

namespace ArquitecturaBase.Application.Interfaces.Persistence;

/// <summary>
/// Los códigos se buscan por destino, propósito y cuenta, pero el lock y los límites son por destino, compartidos entre
/// propósitos y entre cuentas: protegen a quien recibe los mensajes, sea cual sea el motivo (sección 6.3 del spec del
/// ingreso con WhatsApp). La cuenta es la que pidió un código de <see cref="LoginCodePurpose.VerifyDestination"/>, y
/// null con <see cref="LoginCodePurpose.SignIn"/>: así, si otra cuenta pide un código para vincular el mismo número, no
/// invalida el de quien lo estaba vinculando.
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
    /// El último código de ese destino y propósito, pedido por <paramref name="requestedByUserId"/> (null con
    /// <see cref="LoginCodePurpose.SignIn"/>), que no fue reemplazado por uno nuevo (puede estar vencido o usado).
    /// </summary>
    Task<LoginCode?> GetLatestAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Los códigos todavía activos de ese destino y propósito, pedidos por <paramref name="requestedByUserId"/> (null
    /// con <see cref="LoginCodePurpose.SignIn"/>), para invalidarlos cuando esa misma cuenta, o alguien que quiere
    /// entrar, pide uno nuevo.
    /// </summary>
    Task<IReadOnlyList<LoginCode>> ListActiveAsync(
        LoginCodeDestination destination,
        LoginCodePurpose purpose,
        Guid? requestedByUserId,
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

    /// <summary>
    /// Cuándo se mandaron los últimos <paramref name="count"/> códigos de ese canal después de
    /// <paramref name="sinceUtc"/>, del más nuevo al más viejo: a cualquier destino y con cualquier propósito. Solo
    /// cuentan los que salieron (<see cref="LoginCode.SentAtUtc"/>). Es lo que se le paga al canal, y lo usa el tope
    /// diario de WhatsApp (sección 13 del spec del ingreso con WhatsApp).
    /// </summary>
    Task<IReadOnlyList<DateTime>> ListLatestSentTimesAsync(
        LoginCodeChannel channel,
        DateTime sinceUtc,
        int count,
        CancellationToken cancellationToken);

    void Add(LoginCode loginCode);
}
