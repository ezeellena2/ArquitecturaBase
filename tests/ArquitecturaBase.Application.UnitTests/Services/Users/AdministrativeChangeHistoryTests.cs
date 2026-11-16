using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

/// <summary>
/// La historia de los cambios administrativos de una cuenta (N13): quién, sobre qué cuenta, los roles que entraron y
/// salieron y el estado antes y después, escrita adentro del límite del cambio (el repositorio en memoria lanza afuera) y
/// sólo si el cambio pasó. Del correo y del número queda el estado, nunca el dato.
/// </summary>
public sealed class AdministrativeChangeHistoryTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_account_records_who_created_it_with_which_roles()
    {
        var host = new UserServiceTestHost();

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest("ana@example.com", "Ana", [SystemRoles.Admin]), Ct);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(host.Changes.Changes);
        Assert.Equal(AdministrativeChangeKind.UserRolesChanged, change.Kind);
        Assert.Equal(host.CurrentUser.UserId, change.ActorUserId);
        Assert.Equal(result.Value.Id, change.TargetId);
        Assert.Null(change.TargetName);
        Assert.Equal([SystemRoles.Admin], change.Added);
        Assert.Empty(change.Removed);
        Assert.Equal(host.Clock.GetUtcNow().UtcDateTime, change.OccurredAtUtc);
    }

    [Fact]
    public async Task Restoring_an_account_records_the_restore_the_roles_it_got_back_and_the_email_it_lost_verified()
    {
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("vuelve@example.com");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(deleted.Email, "Vuelve", [SystemRoles.User]), Ct);

        Assert.True(result.Value.Restored);
        Assert.Collection(
            host.Changes.Changes,
            restored =>
            {
                Assert.Equal(AdministrativeChangeKind.UserRestored, restored.Kind);
                Assert.Equal(("Deleted", "Active"), (restored.Before, restored.After));
                Assert.Equal([SystemRoles.User], restored.Added);
            },
            roles =>
            {
                Assert.Equal(AdministrativeChangeKind.UserRolesChanged, roles.Kind);
                Assert.Equal([SystemRoles.User], roles.Added);
            },
            email =>
            {
                // El mismo correo, pero el alta lo deja sin verificar (N4).
                Assert.Equal(AdministrativeChangeKind.UserEmailChanged, email.Kind);
                Assert.Equal(("Verified", "Unverified"), (email.Before, email.After));
            });
        Assert.All(host.Changes.Changes, change => Assert.Equal(deleted.Id, change.TargetId));
    }

    [Fact]
    public async Task Restoring_by_the_phone_with_another_email_records_both_contacts_and_names_the_roles_it_comes_back_with()
    {
        // N4: el celular de la empresa pasa de Diego a Nora. La restauración nombra los roles con los que vuelve, y el
        // correo y el número dejan su fila: el correo es otro y el número perdió la verificación. Que los roles que no
        // cambian no dejen fila de roles lo prueba la integración: el doble olvida los roles de una cuenta borrada.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("diego@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(deleted.Id, SystemRoles.User);
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest("nora@example.com", "Nora", [SystemRoles.User], new PhoneNumberInput("AR", deleted.PhoneNumber)),
            Ct);

        Assert.True(result.Value.Restored);
        var restored = Assert.Single(host.Changes.Changes, change => change.Kind == AdministrativeChangeKind.UserRestored);
        Assert.Equal([SystemRoles.User], restored.Added);
        var email = Assert.Single(host.Changes.Changes, change => change.Kind == AdministrativeChangeKind.UserEmailChanged);
        Assert.Equal(("Verified", "Unverified"), (email.Before, email.After));
        var phone = Assert.Single(host.Changes.Changes, change => change.Kind == AdministrativeChangeKind.UserPhoneChanged);
        Assert.Equal(("Verified", "Unverified"), (phone.Before, phone.After));
        Assert.All(host.Changes.Changes, change =>
            Assert.DoesNotContain(change.Added.Concat(change.Removed).Append(change.Before).Append(change.After),
                text => text?.Contains('@', StringComparison.Ordinal) == true || text?.Contains("5550101", StringComparison.Ordinal) == true));
    }

    [Fact]
    public async Task Restoring_by_the_email_records_the_phone_it_lost()
    {
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(deleted.Id, SystemRoles.User);
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(deleted.Email, "Ana", null), Ct);

        Assert.True(result.Value.Restored);
        var phone = Assert.Single(host.Changes.Changes, change => change.Kind == AdministrativeChangeKind.UserPhoneChanged);
        Assert.Equal(("Verified", "None"), (phone.Before, phone.After));
    }

    [Fact]
    public async Task Giving_and_taking_roles_records_both_and_changing_the_contact_keeps_only_its_state()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("carlos@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Administration.UpdateUserAsync(
            new UpdateUserRequest(user.Id, "Carlos", [SystemRoles.Admin], 1, Email: "otro@example.com"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            host.Changes.Changes,
            roles =>
            {
                Assert.Equal(AdministrativeChangeKind.UserRolesChanged, roles.Kind);
                Assert.Equal([SystemRoles.Admin], roles.Added);
                Assert.Equal([SystemRoles.User], roles.Removed);
            },
            email =>
            {
                Assert.Equal(AdministrativeChangeKind.UserEmailChanged, email.Kind);
                Assert.Equal(("Verified", "Unverified"), (email.Before, email.After));
            });
        Assert.All(host.Changes.Changes, change =>
        {
            Assert.Equal(user.Id, change.TargetId);
            Assert.Equal(host.CurrentUser.UserId, change.ActorUserId);
            Assert.DoesNotContain(change.Added.Concat(change.Removed).Append(change.Before).Append(change.After),
                text => text?.Contains('@', StringComparison.Ordinal) == true);
        });
    }

    [Fact]
    public async Task An_edit_that_changes_nothing_or_is_rejected_leaves_no_history()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("igual@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var same = await host.Administration.UpdateUserAsync(
            new UpdateUserRequest(user.Id, "Igual", [SystemRoles.User], 1, Email: "igual@example.com"), Ct);
        var stale = await host.Administration.UpdateUserAsync(
            new UpdateUserRequest(user.Id, "Igual", [SystemRoles.Admin], 1), Ct);

        Assert.True(same.IsSuccess);
        Assert.Equal(UserErrors.RevisionConflict, stale.Error);
        Assert.Empty(host.Changes.Changes);
    }

    [Fact]
    public async Task Deactivating_activating_and_deleting_record_the_status_before_and_after()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("estado@example.com");

        Assert.True((await host.Access.SetUserActiveAsync(user.Id, isActive: false, Ct)).IsSuccess);
        Assert.True((await host.Access.SetUserActiveAsync(user.Id, isActive: false, Ct)).IsSuccess);
        Assert.True((await host.Access.SetUserActiveAsync(user.Id, isActive: true, Ct)).IsSuccess);
        Assert.True((await host.Access.DeleteUserAsync(user.Id, Ct)).IsSuccess);

        Assert.Equal(
            [
                (AdministrativeChangeKind.UserStatusChanged, "Active", "Inactive"),
                (AdministrativeChangeKind.UserStatusChanged, "Inactive", "Active"),
                (AdministrativeChangeKind.UserDeleted, "Active", "Deleted"),
            ],
            host.Changes.Changes.Select(change => (change.Kind, change.Before, change.After)));
    }

    [Fact]
    public async Task Removing_the_own_account_is_rejected_and_leaves_no_history()
    {
        var host = new UserServiceTestHost();
        var self = host.Accounts.AddUser("yo@example.com");
        host.CurrentUser.UserId = self.Id;

        var result = await host.Access.DeleteUserAsync(self.Id, Ct);

        Assert.Equal(UserErrors.CannotModifySelf, result.Error);
        Assert.Empty(host.Changes.Changes);
    }

    [Fact]
    public async Task Unlinking_the_phone_of_another_account_records_only_its_state()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("numero@example.com", phoneNumber: "+5493515550101");

        var result = await host.Access.UnlinkUserPhoneAsync(user.Id, Ct);

        Assert.True(result.IsSuccess);
        var change = Assert.Single(host.Changes.Changes);
        Assert.Equal(AdministrativeChangeKind.UserPhoneChanged, change.Kind);
        Assert.Equal(("Verified", "None"), (change.Before, change.After));
        Assert.DoesNotContain("5550101", change.Before + change.After, StringComparison.Ordinal);
    }
}
