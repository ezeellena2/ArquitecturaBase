using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;

namespace ArquitecturaBase.Application.Services.Users;

/// <summary>
/// Corta todo acceso ya emitido de una cuenta, en dos pasos explícitos dentro de la transacción del caso de uso:
/// 1) invalida los enlaces de ingreso pendientes, también los vencidos, porque uno que el bot mandó antes del corte no
///    puede volver a servir si la cuenta se reactiva dentro de sus 10 minutos, ni después de desvincular un número cuyo
///    chat puede ya no ser de esta persona;
/// 2) renueva el security stamp y revoca autorizaciones y tokens de OpenIddict.
/// Las revocaciones son UPDATE inmediatos: un rollback posterior también las deshace. Las invalidaciones las baja el
/// guardado final del límite; no dependen del guardado del stamp. Lo usan desactivar, borrar y el desvincular del
/// administrador. PhoneNumberChange.VoidPendingLinksAsync (solo los activos) es otra semántica y no se toca.
/// </summary>
internal sealed class AccountAccessRevoker(
    ILoginLinkRepository loginLinks, IIdentityService identity, TimeProvider timeProvider)
{
    public async Task RevokeAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var link in await loginLinks.ListPendingAsync(userId, cancellationToken))
        {
            link.Invalidate(nowUtc);
        }

        await identity.RevokeSessionsAsync(userId, cancellationToken);
    }
}
