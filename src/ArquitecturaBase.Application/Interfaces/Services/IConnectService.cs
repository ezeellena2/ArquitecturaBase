using ArquitecturaBase.Application.Models.Auth;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IConnectService
{
    Task<ConnectUserResponse?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Revoca los tokens de esa autorización (el cierre de sesión). Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a
    /// propósito: es un solo UPDATE de OpenIddict, en autocommit, y no devuelve Result.
    /// </summary>
    Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
}
