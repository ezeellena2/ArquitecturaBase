using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Services.Roles;
using ArquitecturaBase.Application.Services.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserGuardTests
{
    private const string HelpDesk = "Mesa de ayuda";
    private const string Readers = "Lectores";
    private const string RoleManagers = "Roles";

    private readonly InMemoryUserAccounts _accounts = new();
    private readonly UserServiceTestHost.FakeRoleReader _roles;
    private readonly FakeInitialAdmin _initialAdmin = new();

    public UserGuardTests() => _roles = NewRoles(_accounts);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Nobody_can_take_the_admin_role_from_themselves()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Keeping_the_admin_role_while_changing_the_rest_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin, [SystemRoles.Admin, SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Taking_the_admin_role_from_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        AddUser("beto@example.com");
        var guards = GuardsFor(ActorWith(SystemRoles.Admin));

        var result = await guards.EnsureRolesCanChangeAsync(admin, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Taking_the_admin_role_is_allowed_when_another_active_admin_remains()
    {
        var admin = AddAdmin("ana@example.com");
        var second = AddAdmin("beto@example.com");
        var guards = GuardsFor(second.Id);

        var result = await guards.EnsureRolesCanChangeAsync(admin, [SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Nobody_can_deactivate_or_delete_their_own_account()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(admin, Ct);

        Assert.Equal(UserErrors.CannotModifySelfCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_the_last_active_admin_is_rejected()
    {
        var admin = AddAdmin("ana@example.com");
        AddUser("beto@example.com");
        var guards = GuardsFor(ActorWith(SystemRoles.Admin));

        var result = await guards.EnsureCanBeRemovedAsync(admin, Ct);

        Assert.Equal(UserErrors.LastAdminCode, result.Error.Code);
    }

    [Fact]
    public async Task Removing_a_user_without_the_admin_role_is_allowed()
    {
        var admin = AddAdmin("ana@example.com");
        var other = AddUser("beto@example.com");
        var guards = GuardsFor(admin.Id);

        var result = await guards.EnsureCanBeRemovedAsync(other, Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task An_admin_that_is_already_inactive_is_not_the_last_one()
    {
        var inactive = _accounts.AddUser("ana@example.com", isActive: false);
        _accounts.SetRoles(inactive.Id, SystemRoles.Admin);
        AddUser("beto@example.com");
        var guards = GuardsFor(ActorWith(SystemRoles.Admin));

        var result = await guards.EnsureCanBeRemovedAsync(inactive, Ct);

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
        _accounts.LinkExternalLogin(user.Id, ExternalLoginProviders.Google, "google-key");

        Assert.True(await GuardsFor(user.Id).HasOtherLoginMethodAsync(user, Ct));
    }

    [Fact]
    public async Task An_account_with_only_its_number_has_no_other_way_to_sign_in()
    {
        var user = WithPhone(email: null, emailConfirmed: false);

        // Otro proveedor externo que no es Google no cuenta: hoy no hay ninguno, y no se lo ofrece para entrar.
        _accounts.LinkExternalLogin(user.Id, "Other", "other-key");

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

    [Fact]
    public async Task Requested_roles_must_all_exist()
    {
        var guard = GuardsFor(Guid.CreateVersion7());

        Assert.True((await guard.EnsureRolesExistAsync([SystemRoles.Admin, SystemRoles.User], Ct)).IsSuccess);
        Assert.Equal(RoleErrors.NotFound, (await guard.EnsureRolesExistAsync([SystemRoles.User, "admin"], Ct)).Error);
    }

    [Fact]
    public async Task Nobody_gives_a_role_with_permissions_they_do_not_have()
    {
        var target = AddUser("beto@example.com");
        var guards = GuardsFor(ActorWith(HelpDesk));

        var toOther = await guards.EnsureRolesCanChangeAsync(target, [SystemRoles.User, SystemRoles.Admin], Ct);
        var created = await guards.EnsureRolesCanBeGivenAsync([SystemRoles.Admin], email: null, Ct);

        Assert.Equal(UserErrors.RolesNotHeld, toOther.Error);
        Assert.Equal(UserErrors.RolesNotHeld, created.Error);
    }

    [Fact]
    public async Task Nobody_gives_themselves_a_role_with_permissions_they_do_not_have()
    {
        var actor = AddWithRole("lucia@example.com", HelpDesk);

        var result = await GuardsFor(actor.Id).EnsureRolesCanChangeAsync(actor, [HelpDesk, SystemRoles.Admin], Ct);

        Assert.Equal(UserErrors.RolesNotHeld, result.Error);
    }

    [Fact]
    public async Task Roles_whose_permissions_are_all_held_can_be_given_and_taken_away()
    {
        var target = AddUser("beto@example.com");
        _accounts.SetRoles(target.Id, SystemRoles.User, Readers);
        var guards = GuardsFor(ActorWith(HelpDesk));

        Assert.True((await guards.EnsureRolesCanChangeAsync(target, [SystemRoles.User, HelpDesk], Ct)).IsSuccess);
        Assert.True((await guards.EnsureRolesCanBeGivenAsync([SystemRoles.User, Readers], email: null, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Nobody_takes_away_a_role_with_permissions_they_do_not_have()
    {
        var target = AddUser("beto@example.com");
        _accounts.SetRoles(target.Id, SystemRoles.User, RoleManagers);

        var result = await GuardsFor(ActorWith(HelpDesk)).EnsureRolesCanChangeAsync(target, [SystemRoles.User], Ct);

        Assert.Equal(UserErrors.RolesNotHeld, result.Error);
    }

    [Fact]
    public async Task A_permission_outside_the_catalog_neither_gives_nor_asks_for_anything()
    {
        // El claim de un módulo quitado no cuenta: quitar ese rol no pide un permiso que ya no existe.
        _roles.RoleNames.Add("Viejo");
        _roles.Permissions["Viejo"] = ["retired.manage", Permissions.Users.Read];
        var target = AddUser("beto@example.com");
        _accounts.SetRoles(target.Id, SystemRoles.User, "Viejo");

        var result = await GuardsFor(ActorWith(HelpDesk)).EnsureRolesCanChangeAsync(target, [SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Nobody_acts_on_an_account_with_permissions_they_do_not_have()
    {
        var admin = AddAdmin("ana@example.com");
        AddAdmin("carla@example.com");
        var guards = GuardsFor(ActorWith(HelpDesk));
        var email = Email.Create("lucia@example.com").Value;
        var phone = PhoneNumber.Create("+5493515550199").Value;

        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsureContactCanChangeAsync(admin, email, null, Ct)).Error);
        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsureContactCanChangeAsync(admin, null, phone, Ct)).Error);
        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsureCanBeRemovedAsync(admin, Ct)).Error);
        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsurePhoneCanBeUnlinkedAsync(admin, Ct)).Error);
    }

    [Fact]
    public async Task An_account_whose_permissions_are_all_held_can_be_acted_on()
    {
        var target = AddUser("beto@example.com");
        _accounts.SetRoles(target.Id, SystemRoles.User, Readers);
        var guards = GuardsFor(ActorWith(HelpDesk));
        var email = Email.Create("beto.nuevo@example.com").Value;

        Assert.True((await guards.EnsureContactCanChangeAsync(target, email, null, Ct)).IsSuccess);
        Assert.True((await guards.EnsureCanBeRemovedAsync(target, Ct)).IsSuccess);
        Assert.True((await guards.EnsurePhoneCanBeUnlinkedAsync(target, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Giving_the_initial_admin_email_counts_as_giving_admin()
    {
        // El arranque le da Admin a la cuenta de Seed:AdminEmail: ponérselo a una cuenta es dárselo.
        var target = AddUser("beto@example.com");
        var seedEmail = Email.Create(FakeInitialAdmin.DefaultEmail).Value;
        var helpDesk = GuardsFor(ActorWith(HelpDesk));
        var admin = GuardsFor(ActorWith(SystemRoles.Admin));

        Assert.Equal(UserErrors.RolesNotHeld, (await helpDesk.EnsureRolesCanBeGivenAsync([SystemRoles.User], seedEmail, Ct)).Error);
        Assert.Equal(UserErrors.RolesNotHeld, (await helpDesk.EnsureContactCanChangeAsync(target, seedEmail, null, Ct)).Error);
        Assert.True((await admin.EnsureRolesCanBeGivenAsync([SystemRoles.User], seedEmail, Ct)).IsSuccess);
        Assert.True((await admin.EnsureContactCanChangeAsync(target, seedEmail, null, Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_initial_admin_keeps_admin_and_its_email()
    {
        var initial = AddAdmin(FakeInitialAdmin.DefaultEmail);
        AddAdmin("carla@example.com");
        var guards = GuardsFor(ActorWith(SystemRoles.Admin));
        var otherEmail = Email.Create("otro@example.com").Value;
        var phone = PhoneNumber.Create("+5493515550199").Value;

        Assert.Equal(UserErrors.InitialAdmin, (await guards.EnsureRolesCanChangeAsync(initial, [SystemRoles.User], Ct)).Error);
        Assert.Equal(UserErrors.InitialAdmin, (await guards.EnsureContactCanChangeAsync(initial, otherEmail, null, Ct)).Error);
        Assert.True((await guards.EnsureContactCanChangeAsync(initial, null, phone, Ct)).IsSuccess);
        Assert.True((await guards.EnsureRolesCanChangeAsync(initial, [SystemRoles.Admin, SystemRoles.User], Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_initial_admin_account_counts_as_an_administrator_even_without_admin()
    {
        // Una cuenta con el correo de Seed:AdminEmail y sólo User (por ejemplo, restaurada): el arranque le da Admin.
        var initial = AddUser(FakeInitialAdmin.DefaultEmail);
        var guards = GuardsFor(ActorWith(HelpDesk));
        var phone = PhoneNumber.Create("+5493515550199").Value;

        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsureContactCanChangeAsync(initial, null, phone, Ct)).Error);
        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsurePhoneCanBeUnlinkedAsync(initial, Ct)).Error);
        Assert.Equal(UserErrors.HasMorePermissions, (await guards.EnsureCanBeRemovedAsync(initial, Ct)).Error);
        Assert.True((await GuardsFor(ActorWith(SystemRoles.Admin)).EnsureContactCanChangeAsync(initial, null, phone, Ct)).IsSuccess);
    }

    [Fact]
    public async Task The_permissions_of_user_count_as_held_by_whoever_asks()
    {
        // User lo recibe toda cuenta nueva y sólo lo edita Admin: lo que tiene, lo tiene cualquiera.
        _roles.Permissions[SystemRoles.User] = [Permissions.Settings.Manage];
        var target = AddUser("beto@example.com");
        var guards = GuardsFor(ActorWith(HelpDesk));

        Assert.True((await guards.EnsureRolesCanBeGivenAsync([SystemRoles.User], email: null, Ct)).IsSuccess);
        Assert.True((await guards.EnsureRolesCanChangeAsync(target, [Readers], Ct)).IsSuccess);
        Assert.True((await guards.EnsureContactCanChangeAsync(
            target, Email.Create("beto.nuevo@example.com").Value, null, Ct)).IsSuccess);
        Assert.True((await guards.EnsureCanBeRemovedAsync(target, Ct)).IsSuccess);
    }

    [Fact]
    public async Task Without_a_seed_admin_email_nobody_is_the_initial_admin()
    {
        var initial = AddAdmin(FakeInitialAdmin.DefaultEmail);
        AddAdmin("carla@example.com");
        _initialAdmin.AdminEmail = null;

        var result = await GuardsFor(ActorWith(SystemRoles.Admin)).EnsureRolesCanChangeAsync(initial, [SystemRoles.User], Ct);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// «Mesa de ayuda» administra cuentas, «Lectores» sólo las ve y «Roles» administra roles: los roles delegados de N1.
    /// </summary>
    private static UserServiceTestHost.FakeRoleReader NewRoles(InMemoryUserAccounts accounts)
    {
        var roles = new UserServiceTestHost.FakeRoleReader(accounts);
        roles.RoleNames.AddRange([HelpDesk, Readers, RoleManagers]);
        roles.Permissions[HelpDesk] = [Permissions.Users.Read, Permissions.Users.Manage];
        roles.Permissions[Readers] = [Permissions.Users.Read];
        roles.Permissions[RoleManagers] = [Permissions.Roles.Read, Permissions.Roles.Manage];

        return roles;
    }

    private UserGuard GuardsFor(Guid currentUserId)
    {
        var currentUser = new FakeCurrentUser { UserId = currentUserId };

        return new(
            currentUser,
            _accounts,
            _accounts,
            _roles,
            _roles,
            new PermissionDelegationGuard(currentUser, _roles, _roles.Catalog, _initialAdmin),
            _initialAdmin);
    }

    /// <summary>Quien pide, con esos roles y sin ser una cuenta del listado: no cambia el conteo de administradores.</summary>
    private Guid ActorWith(params string[] roles)
    {
        var actorId = Guid.CreateVersion7();
        _accounts.SetRoles(actorId, roles);

        return actorId;
    }

    private static UserAccount WithPhone(string? email, bool emailConfirmed) =>
        new(
            Guid.CreateVersion7(),
            email,
            emailConfirmed,
            "+5493515550101",
            PhoneNumberConfirmed: true,
            DisplayName: null,
            "es",
            InMemoryUserAccounts.DefaultTimeZoneId,
            IsActive: true,
            Revision: 1);

    private UserAccount AddAdmin(string email) => AddWithRole(email, SystemRoles.Admin);

    private UserAccount AddUser(string email) => AddWithRole(email, SystemRoles.User);

    private UserAccount AddWithRole(string email, string role)
    {
        var user = _accounts.AddUser(email);
        _accounts.SetRoles(user.Id, role);

        return user;
    }
}
