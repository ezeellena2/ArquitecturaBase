using ArquitecturaBase.Application.Interfaces.Integrations;
using OpenIddict.Abstractions;

namespace ArquitecturaBase.Infrastructure.Identity.OpenIddict;

internal sealed class OpenIddictTokenRevoker(IOpenIddictTokenManager tokens) : IOpenIddictTokenRevoker
{
    public async Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        await tokens.RevokeByAuthorizationIdAsync(authorizationId, cancellationToken);
}
