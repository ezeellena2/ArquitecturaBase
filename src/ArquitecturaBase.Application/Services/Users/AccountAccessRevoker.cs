using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Corta todo acceso ya emitido de una cuenta, en dos pasos dentro de la transacción del caso de uso:
/// <list type="number">
/// <item>invalida los enlaces de ingreso pendientes, también los vencidos, porque uno que el bot mandó antes del corte
/// no puede volver a servir si la cuenta se reactiva dentro de sus 10 minutos, ni después de desvincular un número
/// cuyo chat puede ya no ser de esta persona;</item>
/// <item>renueva el security stamp y revoca autorizaciones y tokens de OpenIddict.</item>
/// </list>
/// Las revocaciones son UPDATE inmediatos: un rollback posterior también las deshace. Las invalidaciones las baja el
/// guardado final del límite. A diferencia de <see cref="PhoneNumberLinker.VoidPendingLinksAsync"/>, que invalida solo
/// los activos porque la cuenta sigue con acceso, acá caen también los vencidos.
/// </summary>
/// <remarks>
/// Quien llama ya tomó el lock de enlaces de la cuenta (<see cref="LockAsync"/>, o
/// <see cref="PhoneNumberLinker.LockAsync"/>, que lo toma después de los participantes del número): sin él, un enlace
/// emitido en paralelo quedaría fuera de la revocación. El lock es un paso aparte porque el orden global (primero los
/// locks de los participantes del número, después la cuenta) lo decide quien llama.
/// </remarks>
internal sealed class AccountAccessRevoker(
    ILoginLinkRepository loginLinks, ISignInService signIn, TimeProvider timeProvider)
{
    /// <summary>
    /// El lock de enlaces de la cuenta (<see cref="ILoginLinkRepository.LockAccountAsync"/>), hasta el final del límite:
    /// pone en fila la revocación con la emisión y el canje de enlaces de la cuenta.
    /// </summary>
    public Task LockAsync(Guid userId, CancellationToken cancellationToken) =>
        loginLinks.LockAccountAsync(userId, cancellationToken);

    public async Task RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var link in await loginLinks.ListPendingAsync(userId, cancellationToken))
        {
            link.Invalidate(nowUtc);
        }

        await signIn.RevokeSessionsAsync(userId, cancellationToken);
    }
}
