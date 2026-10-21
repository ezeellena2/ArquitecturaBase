using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Interfaces.Integrations.Request;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures;

internal sealed class PermissionAuthorizationFixture(params string[] granted) : ICurrentUser, IPermissionService
{
    public Guid? UserId { get; set; } = Guid.CreateVersion7();
    public bool IsAuthenticated => UserId is not null;
    public CancellationToken ObservedToken { get; private set; }
    public Task<IReadOnlyCollection<string>> GetPermissionsAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>(granted);
    public Task<bool> HasPermissionAsync(Guid userId, string permission, CancellationToken cancellationToken)
    {
        ObservedToken = cancellationToken;
        return Task.FromResult(granted.Contains(permission, StringComparer.Ordinal));
    }
}
