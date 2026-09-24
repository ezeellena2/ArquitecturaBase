using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

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

    [Fact]
    public async Task A_verified_email_is_another_way_to_sign_in()
    {
        var user = WithPhone(email: "ana@example.com", emailConfirmed: true);

        Assert.True(await GuardsFor(user.Id).HasOtherLoginMethodAsync(user, Ct));
    }

    [Fact]
    public async Task An_email_that_is_not_verified_is_not_another_way_to_sign_in()
    {
        // Lo cargó un administrador y la persona nunca entró con él: no está probado que lo lea.
        var user = WithPhone(email: "ana@example.com", emailConfirmed: false);

        Assert.False(await GuardsFor(user.Id).HasOtherLoginMethodAsync(user, Ct));
    }

    [Fact]
    public async Task A_linked_google_account_is_another_way_to_sign_in()
    {
        var user = WithPhone(email: null, emailConfirmed: false);
        _identity.LinkExternalLogin(user.Id, ExternalLoginProviders.Google, "google-key");

        Assert.True(await GuardsFor(user.Id).HasOtherLoginMethodAsync(user, Ct));
    }

    [Fact]
    public async Task An_account_with_only_its_number_has_no_other_way_to_sign_in()
    {
        var user = WithPhone(email: null, emailConfirmed: false);

        // Otro proveedor externo que no es Google no cuenta: hoy no hay ninguno, y no se lo ofrece para entrar.
        _identity.LinkExternalLogin(user.Id, "Other", "other-key");

        Assert.False(await GuardsFor(user.Id).HasOtherLoginMethodAsync(user, Ct));
    }

    [Fact]
    public async Task An_administrator_can_leave_someone_else_without_a_way_to_sign_in()
    {
        // El teléfono robado: la pantalla se lo advierte, pero la regla no lo impide.
        var user = WithPhone(email: null, emailConfirmed: false);

        Assert.True((await GuardsFor(Guid.CreateVersion7()).EnsurePhoneCanBeUnlinkedAsync(user, Ct)).IsSuccess);
    }

    [Fact]
    public async Task An_administrator_does_not_unlink_their_own_only_way_to_sign_in()
    {
        var user = WithPhone(email: null, emailConfirmed: false);

        var result = await GuardsFor(user.Id).EnsurePhoneCanBeUnlinkedAsync(user, Ct);

        Assert.Equal(UserErrors.LastLoginMethodCode, result.Error.Code);
    }

    [Fact]
    public async Task An_administrator_with_a_verified_email_can_unlink_their_own_number()
    {
        var user = WithPhone(email: "ana@example.com", emailConfirmed: true);

        Assert.True((await GuardsFor(user.Id).EnsurePhoneCanBeUnlinkedAsync(user, Ct)).IsSuccess);
    }

    private UserGuards GuardsFor(Guid currentUserId) =>
        new(new FakeCurrentUser { UserId = currentUserId }, _identity);

    private static UserAccount WithPhone(string? email, bool emailConfirmed) =>
        new(
            Guid.CreateVersion7(),
            email,
            emailConfirmed,
            "+5493515550101",
            PhoneNumberConfirmed: true,
            DisplayName: null,
            "es",
            FakeIdentityService.DefaultTimeZoneId,
            IsActive: true);

    private UserAccount AddAdmin(string email) => AddWithRole(email, SystemRoles.Admin);

    private UserAccount AddUser(string email) => AddWithRole(email, SystemRoles.User);

    private UserAccount AddWithRole(string email, string role)
    {
        var user = _identity.AddUser(email);
        _identity.SetRoles(user.Id, role);

        return user;
    }
}
