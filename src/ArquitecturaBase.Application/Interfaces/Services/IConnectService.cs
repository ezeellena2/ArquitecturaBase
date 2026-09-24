using ArquitecturaBase.Application.Models.Auth;

namespace ArquitecturaBase.Application.Interfaces.Services;

public interface IConnectService
{
    Task<ConnectUser?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken);

    Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
}
