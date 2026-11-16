using ArquitecturaBase.Application.Models.Identity;

namespace ArquitecturaBase.Application.Interfaces.Integrations.Identity;

/// <summary>
/// N16: poda las autorizaciones y los tokens de OpenIddict que vencieron antes de <c>expiredBeforeUtc</c>, con la poda
/// que trae OpenIddict, de a tramos. Cada tramo participa del límite del llamador, que ya tomó el lock de la retención.
/// </summary>
public interface IOpenIddictPruner
{
    /// <summary>
    /// Un tramo: hasta un día después del token más viejo creado desde <paramref name="fromUtc"/> (o desde el principio,
    /// si es null), sin pasar el umbral final. El próximo tramo sigue desde <see cref="OpenIddictPruneTranche.NextFromUtc"/>.
    /// </summary>
    Task<OpenIddictPruneTranche> PruneTrancheAsync(DateTime expiredBeforeUtc, DateTime? fromUtc, CancellationToken cancellationToken);
}
