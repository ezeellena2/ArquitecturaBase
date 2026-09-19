using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Domain.Authentication;

public interface ILoginCodeRepository
{
    /// <summary>
    /// Pone en fila los pedidos y las verificaciones de códigos de un mismo email hasta que termine la unidad de
    /// trabajo. Sin esto, dos requests simultáneas leen el mismo estado y se saltean los límites de la sección 5.3.
    /// </summary>
    Task LockEmailAsync(Email email, CancellationToken cancellationToken);

    /// <summary>El último código de ese email que no fue reemplazado por uno nuevo (puede estar vencido o usado).</summary>
    Task<LoginCode?> GetLatestAsync(Email email, CancellationToken cancellationToken);

    /// <summary>Los códigos todavía activos de ese email, para invalidarlos cuando se pide uno nuevo.</summary>
    Task<IReadOnlyList<LoginCode>> ListActiveAsync(Email email, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Cuándo se pidió cada código de ese email desde <paramref name="sinceUtc"/>, del más viejo al más nuevo.</summary>
    Task<IReadOnlyList<DateTime>> ListRequestTimesSinceAsync(Email email, DateTime sinceUtc, CancellationToken cancellationToken);

    void Add(LoginCode loginCode);
}
