using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Services;
using ArquitecturaBase.Application.Models.Auth;

namespace ArquitecturaBase.Application.Services.Auth;

internal sealed class ConnectService(IUserReader users, IOpenIddictTokenRevoker tokenRevoker) : IConnectService
{
    public async Task<ConnectUser?> GetActiveUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(userId, cancellationToken);
        if (account is not { IsActive: true })
        {
            return null;
        }

        var roles = await users.ListRoleNamesForUserAsync(userId, cancellationToken);
        return new ConnectUser(account, [.. roles.Order(StringComparer.Ordinal)]);
    }

    public Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(authorizationId);
        return tokenRevoker.RevokeAuthorizationAsync(authorizationId, cancellationToken);
    }
}
