namespace ArquitecturaBase.Application.Interfaces.Integrations;

/// <summary>Revoca los tokens emitidos para una autorización OIDC.</summary>
public interface IOpenIddictTokenRevoker
{
    Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
}
