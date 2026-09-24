using ArquitecturaBase.Application.Interfaces.Integrations;
using ArquitecturaBase.Application.Services.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;

namespace ArquitecturaBase.Application.UnitTests.Services.Auth;

public sealed class ConnectServiceTests
{
    private readonly FakeIdentityService _users = new();
    private readonly FakeTokenRevoker _tokens = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Active_user_exposes_current_account_and_sorted_roles()
    {
        var user = _users.AddUser("ana@example.com");
        _users.SetRoles(user.Id, "User", "Admin");

        var result = await Service().GetActiveUserAsync(user.Id, Ct);

        Assert.Equal(user, result?.Account);
        Assert.Equal(["Admin", "User"], result?.Roles);
    }

    [Fact]
    public async Task Missing_or_disabled_user_cannot_receive_tokens()
    {
        var disabled = _users.AddUser("ana@example.com", isActive: false);

        Assert.Null(await Service().GetActiveUserAsync(disabled.Id, Ct));
        Assert.Null(await Service().GetActiveUserAsync(Guid.CreateVersion7(), Ct));
    }

    [Fact]
    public async Task Logout_revokes_every_token_in_the_authorization()
    {
        await Service().RevokeAuthorizationAsync("authorization-123", Ct);

        Assert.Equal(["authorization-123"], _tokens.AuthorizationIds);
    }

    private ConnectService Service() => new(_users, _tokens);

    private sealed class FakeTokenRevoker : IOpenIddictTokenRevoker
    {
        public List<string> AuthorizationIds { get; } = [];

        public Task RevokeAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
        {
            AuthorizationIds.Add(authorizationId);
            return Task.CompletedTask;
        }
    }
}
