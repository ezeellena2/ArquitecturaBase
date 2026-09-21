using ArquitecturaBase.Application.Abstractions.Identity;
using ArquitecturaBase.Application.Features.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Features.Users;

public sealed class UserGuardsTests
{
    private readonly FakeIdentityService _identity = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nobody_can_take_the_admin_role_from_themselves()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Keeping_the_admin_role_while_changing_the_rest_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.Admin, SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Taking_the_admin_role_from_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Taking_the_admin_role_is_allowed_when_another_active_admin_remains()
    {
        var admin = AddAdmin("ana@example.com");
        var second = AddAdmin("beto@example.com");
        var guards = GuardsFor(second.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin.Id, [SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Nobody_can_deactivate_or_delete_their_own_account()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureCanBeRemovedAsync(admin.Id, Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_a_user_without_the_admin_role_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(other.Id, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_admin_that_is_already_inactive_is_not_the_last_one()
    {
        var inactive = _identity.AddUser("ana@example.com", isActive: false);
        _identity.SetRoles(inactive.Id, SystemRoles.Admin);
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(other.Id);

        var result = await guards.EnsureCanBeRemovedAsync(inactive.Id, Ct);

        Assert.True(result.IsSuccess);
    }

    private UserGuards GuardsFor(Guid currentUserId) =>
        new(new FakeCurrentUser { UserId = currentUserId }, _identity);

    private UserAccount AddAdmin(string email) => AddWithRole(email, SystemRoles.Admin);

    private UserAccount AddUser(string email) => AddWithRole(email, SystemRoles.User);

    private UserAccount AddWithRole(string email, string role)
    {
        var user = _identity.AddUser(email);
        _identity.SetRoles(user.Id, role);

        return user;
    }
}
