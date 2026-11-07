using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

internal sealed class PermissionAuthorizationFixture(params string[] granted) : ICurrentUser, IPermissionService
{
    public Guid? UserId { get; set; } = Guid.CreateVersion7();
    public bool IsAuthenticated => UserId is not null;
    public CancellationToken ObservedToken { get; private set; }

    /// <summary>Cuántas veces se le preguntó por los permisos de la cuenta, por cualquiera de las dos vías.</summary>
    public int Reads { get; private set; }

    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        Reads++;
        ObservedToken = cancellationToken;
        return Task.FromResult<IReadOnlyCollection<string>>(granted);
    }

    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
    {
        Reads++;
        ObservedToken = cancellationToken;
        return Task.FromResult(granted.Contains(permission, StringComparer.Ordinal));
    }
}
