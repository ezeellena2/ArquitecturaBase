using ArquitecturaBase.Application.Features.Users.GetCurrentUser;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class GetCurrentUserQueryHandlerTests
{
    private readonly FakeIdentityService _identity = new();
    private readonly FakePermissionService _permissions = new();
    private readonly InMemoryLoginAuditRepository _loginAudits = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Returns_the_profile_with_sorted_roles_and_permissions()
    {
        var user = _identity.AddUser("ana@example.com", culture: "en");
        _identity.SetRoles(user.Id, "User", "Admin");
        _permissions.Permissions[user.Id] = ["users.read", "roles.manage"];

        var result = await Handler(user.Id).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(user.Id, result.Value.Id);
        Assert.Equal("ana@example.com", result.Value.Email);
        Assert.Equal("en", result.Value.Culture);
        Assert.Equal(FakeIdentityService.DefaultTimeZoneId, result.Value.TimeZoneId);
        Assert.Equal(["Admin", "User"], result.Value.Roles);
        Assert.Equal(["roles.manage", "users.read"], result.Value.Permissions);
        Assert.Null(result.Value.LastLoginAtUtc);
    }

    [Fact]
    public async Task Unknown_user_is_not_found()
    {
        var result = await Handler(Guid.CreateVersion7()).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    [Fact]
    public async Task Anonymous_request_is_not_found()
    {
        var result = await Handler(userId: null).Handle(new GetCurrentUserQuery(), Ct);

        Assert.Equal(UserErrors.NotFoundCode, result.Error.Code);
    }

    private GetCurrentUserQueryHandler Handler(Guid? userId) =>
        new(new FakeCurrentUser { UserId = userId }, _identity, _permissions, _loginAudits);
}
