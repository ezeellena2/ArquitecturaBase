using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Channels;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserAdministrationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invalid_create_stops_before_locks_identity_and_commit()
    {
        var host = new UserServiceTestHost();

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest("invalid", "Ana", null), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("email"));
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Empty(host.Accounts.Users);
        Assert.Equal(0, host.UnitOfWork.Transactions);
        Assert.Equal(["Handling CreateUser", "CreateUser failed with Validation.Failed"],
            host.AdministrationLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_assigns_default_role_and_confirms_once()
    {
        var host = new UserServiceTestHost();
        var email = "alta@example.com";

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(email, "Ana", null), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(host.Accounts.Users);
        Assert.Equal(new CreateUserResponse(user.Id, Restored: false), result.Value);
        Assert.Equal(email, user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal([SystemRoles.User], await host.Accounts.ListRoleNamesForUserAsync(user.Id, Ct));
        Assert.Equal([email], host.Destinations.LockedDestinations);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);
        Assert.Equal(["Handling CreateUser", "Handled CreateUser"],
            host.AdministrationLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_locks_email_before_phone_and_rejects_unknown_roles_without_mutation()
    {
        var host = new UserServiceTestHost();
        var request = new CreateUserRequest("ana@example.com", "Ana", ["DoesNotExist"],
            new PhoneNumberInput("AR", "+5493515550101"));

        var result = await host.Administration.CreateUserAsync(request, Ct);

        Assert.Equal(RoleErrors.NotFound, result.Error);
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);

        var valid = await host.Administration.CreateUserAsync(request with { Roles = [SystemRoles.User] }, Ct);
        Assert.True(valid.IsSuccess);
        Assert.Equal(["ana@example.com", "+5493515550101"], host.Destinations.LockedDestinations);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restores_a_deleted_account_only_when_both_destinations_belong_to_it()
    {
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("restore@example.com", phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));
        var request = new CreateUserRequest(deleted.Email, "Nuevo nombre", [SystemRoles.Admin],
            new PhoneNumberInput("AR", deleted.PhoneNumber));

        var result = await host.Administration.CreateUserAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(new CreateUserResponse(deleted.Id, Restored: true), result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal("Nuevo nombre", restored.DisplayName);
        Assert.False(restored.EmailConfirmed);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(restored.Id, Ct));
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_by_the_phone_removes_the_email_and_the_google_login_of_the_previous_owner()
    {
        // N4: el número de la empresa pasó a otra persona. La cuenta vuelve sin el correo verificado ni el Google de quien
        // la tenía antes, que así no entra a ver lo de la persona nueva.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("diego@example.com", phoneNumber: "+5493515550101");
        host.Accounts.LinkExternalLogin(deleted.Id, ExternalLoginProviders.Google, "google-diego");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(null, "Nora", null, new PhoneNumberInput("AR", deleted.PhoneNumber)), Ct);

        Assert.Equal(new CreateUserResponse(deleted.Id, Restored: true), result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal("Nora", restored.DisplayName);
        Assert.Null(restored.Email);
        Assert.False(restored.EmailConfirmed);
        Assert.Equal(deleted.PhoneNumber, restored.PhoneNumber);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Null(await host.Accounts.FindByExternalLoginAsync(ExternalLoginProviders.Google, "google-diego", Ct));
        Assert.False(await host.Accounts.ExistsDeletedByEmailAsync(Email.Create("diego@example.com").Value, Ct));
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_by_the_email_removes_the_previous_phone_and_its_participants_release_it()
    {
        // Sin número en el alta, el de antes se quita como al desvincularlo: primero los locks del participante y de los
        // enlaces de la cuenta (después de los del destino), y el participante suelta lo que tenía atado.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.LinkExternalLogin(deleted.Id, ExternalLoginProviders.Google, "google-ana");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(deleted.Email, "Ana", null), Ct);

        Assert.Equal(new CreateUserResponse(deleted.Id, Restored: true), result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal(deleted.Email, restored.Email);
        Assert.False(restored.EmailConfirmed);
        Assert.Null(restored.PhoneNumber);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.False(await host.Accounts.ExistsExternalLoginAsync(deleted.Id, ExternalLoginProviders.Google, Ct));
        Assert.Equal(["ana@example.com"], host.Destinations.LockedDestinations);
        Assert.Equal(
            ["participant-lock:" + deleted.Id, "login-link:" + deleted.Id, "participant-released:" + deleted.Id],
            host.PhoneEvents);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_with_the_same_phone_does_not_release_it()
    {
        // El número sigue en la cuenta: su chat es el de la persona nueva, y no hay nada que soltar.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser(null, phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(null, "Nora", null, new PhoneNumberInput("AR", deleted.PhoneNumber)), Ct);

        Assert.True(result.Value.Restored);
        Assert.Equal(["participant-lock:" + deleted.Id, "login-link:" + deleted.Id], host.PhoneEvents);
    }

    [Fact]
    public async Task Create_restoring_by_the_phone_with_another_email_replaces_the_previous_email()
    {
        // N4: Nora llega con su correo y el celular de la empresa, que era de la cuenta borrada de Diego. La cuenta vuelve
        // sólo con lo que trajo el alta: el correo de Diego se reemplaza por el de Nora, sin verificar.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("diego@example.com", phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest("nora@example.com", "Nora", null, new PhoneNumberInput("AR", deleted.PhoneNumber)), Ct);

        Assert.Equal(new CreateUserResponse(deleted.Id, Restored: true), result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal("nora@example.com", restored.Email);
        Assert.False(restored.EmailConfirmed);
        Assert.Equal(deleted.PhoneNumber, restored.PhoneNumber);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Null(await host.Accounts.FindByEmailAsync(Email.Create("diego@example.com").Value, Ct));
        Assert.False(await host.Accounts.ExistsDeletedByEmailAsync(Email.Create("diego@example.com").Value, Ct));
        Assert.DoesNotContain("participant-released:" + deleted.Id, host.PhoneEvents);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_by_the_email_with_another_phone_replaces_the_previous_one_and_releases_it()
    {
        // El número de la cuenta borrada no es el del alta: se reemplaza, y sus participantes sueltan el de antes, como
        // en la edición.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(deleted.Email, "Ana", null, new PhoneNumberInput("AR", "+5493515550202")), Ct);

        Assert.Equal(new CreateUserResponse(deleted.Id, Restored: true), result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal(deleted.Email, restored.Email);
        Assert.Equal("+5493515550202", restored.PhoneNumber);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Equal(["ana@example.com", "+5493515550202"], host.Destinations.LockedDestinations);
        Assert.Equal(
            ["participant-lock:" + deleted.Id, "login-link:" + deleted.Id, "participant-released:" + deleted.Id],
            host.PhoneEvents);
        Assert.Equal("+5493515550202", Assert.Single(host.Participant.LockedPhones)?.Value);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_does_not_restore_when_the_other_destination_belongs_to_an_active_account()
    {
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        var active = host.Accounts.AddUser("beto@example.com", phoneNumber: "+5493515550202");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(deleted.Email, "Ana", null, new PhoneNumberInput("AR", active.PhoneNumber)), Ct);

        Assert.Equal(UserErrors.PhoneAlreadyExists, result.Error);
        Assert.Equal([active.Id], host.Accounts.Users.Select(user => user.Id));
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_cuts_again_every_access_the_account_had_after_taking_its_locks()
    {
        // Una sesión o un enlace que se salvó del corte del borrado (un refresh token dura 30 días) no vuelve a valer al
        // restaurar: se corta todo otra vez, con el lock de los enlaces de la cuenta ya tomado.
        var host = new UserServiceTestHost();
        var deleted = host.Accounts.AddUser("diego@example.com", phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(deleted.Id, Ct));
        var now = host.Clock.GetUtcNow().UtcDateTime;
        var expired = LoginLink.Issue(deleted.Id, "hash-expired", now.AddHours(-1));
        var active = LoginLink.Issue(deleted.Id, "hash-active", now.AddMinutes(-1));
        host.Links.Links.AddRange([expired, active]);

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(null, "Nora", null, new PhoneNumberInput("AR", deleted.PhoneNumber)), Ct);

        Assert.True(result.Value.Restored);
        Assert.Equal([deleted.Id], host.SignIn.RevokedUsers);
        Assert.Equal(now, expired.InvalidatedAtUtc);
        Assert.Equal(now, active.InvalidatedAtUtc);
        Assert.Equal([deleted.Id], host.Links.LockedAccounts);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_restoring_applies_the_default_preferences_instead_of_those_of_the_previous_owner()
    {
        // Como una cuenta nueva: el idioma y la zona horaria del sistema en ese momento, no los del dueño anterior (la
        // invitación sale en el idioma de la cuenta).
        var host = new UserServiceTestHost();
        host.SystemSettings.Presentation = host.SystemSettings.Presentation with
        {
            DefaultCulture = "es",
            DefaultTimeZoneId = "America/Montevideo",
        };
        var deleted = host.Accounts.AddUser("diego@example.com", phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(async accounts =>
        {
            await accounts.UpdateProfileAsync(deleted.Id, "Diego", "en", "Europe/Madrid", Ct);
            await accounts.DeleteAsync(deleted.Id, Ct);
        });

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(null, "Nora", null, new PhoneNumberInput("AR", deleted.PhoneNumber)), Ct);

        Assert.True(result.Value.Restored);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal("es", restored.Culture);
        Assert.Equal("America/Montevideo", restored.TimeZoneId);
    }

    [Fact]
    public async Task Create_restores_the_deleted_initial_admin_by_its_number_without_its_email_so_it_is_not_admin_again()
    {
        // Restaurar por el número le quita el correo de Seed:AdminEmail: el arranque ya no le devolvería Admin, así que
        // quien sólo administra cuentas la puede restaurar, y vuelve con los roles del alta.
        var host = new UserServiceTestHost();
        ActAsHelpDesk(host);
        var initial = host.Accounts.AddUser(FakeInitialAdmin.DefaultEmail, phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(initial.Id, SystemRoles.Admin);
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(initial.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(null, null, [SystemRoles.User], new PhoneNumberInput("AR", initial.PhoneNumber)), Ct);

        Assert.Equal(new CreateUserResponse(initial.Id, Restored: true), result.Value);
        Assert.Null(Assert.Single(host.Accounts.Users).Email);
        Assert.Equal([SystemRoles.User], await host.Accounts.ListRoleNamesForUserAsync(initial.Id, Ct));
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_still_treats_restoring_the_deleted_initial_admin_by_its_email_as_giving_admin()
    {
        // Con el correo de Seed:AdminEmail en el alta, la cuenta lo conserva y el arranque le devolvería Admin.
        var host = new UserServiceTestHost();
        ActAsHelpDesk(host);
        var initial = host.Accounts.AddUser(FakeInitialAdmin.DefaultEmail, phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(initial.Id, SystemRoles.Admin);
        await host.Accounts.ArrangeAsync(accounts => accounts.DeleteAsync(initial.Id, Ct));

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest(FakeInitialAdmin.DefaultEmail, null, [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.RolesNotHeld, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_checks_again_the_roles_it_assigned_when_one_was_renamed_in_between()
    {
        var host = new UserServiceTestHost();
        ActAsHelpDesk(host);
        RenameDuringAssignment(host);

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest("beto@example.com", "Beto", [Readers]), Ct);

        Assert.Equal(UserErrors.RolesNotHeld, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Update_checks_again_the_roles_it_assigned_when_one_was_renamed_in_between()
    {
        var host = new UserServiceTestHost();
        ActAsHelpDesk(host);
        var user = host.Accounts.AddUser("beto@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        RenameDuringAssignment(host);

        var result = await host.Administration.UpdateUserAsync(
            new UpdateUserRequest(user.Id, "Beto", [SystemRoles.User, Readers]), Ct);

        Assert.Equal(UserErrors.RolesNotHeld, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_with_user_passes_when_user_has_a_permission_the_actor_lacks()
    {
        var host = new UserServiceTestHost();
        ActAsHelpDesk(host);
        host.RoleReader.Permissions[SystemRoles.User] = [Permissions.Settings.Manage];

        var result = await host.Administration.CreateUserAsync(
            new CreateUserRequest("beto@example.com", "Beto", [SystemRoles.User]), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_rejects_destinations_from_two_deleted_accounts_without_commit()
    {
        var host = new UserServiceTestHost();
        var byEmail = host.Accounts.AddUser("email@example.com");
        var byPhone = host.Accounts.AddUser(null, phoneNumber: "+5493515550101");
        await host.Accounts.ArrangeAsync(async accounts =>
        {
            await accounts.DeleteAsync(byEmail.Id, Ct);
            await accounts.DeleteAsync(byPhone.Id, Ct);
        });

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(
            byEmail.Email, "Ana", null, new PhoneNumberInput("AR", byPhone.PhoneNumber)), Ct);

        Assert.Equal(UserErrors.PhoneAlreadyExists, result.Error);
        Assert.Empty(host.Accounts.Users);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Invalid_update_stops_before_locks_and_commit()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com");

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", Roles: null), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("roles"));
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Equal(0, host.UnitOfWork.Transactions);
        Assert.Equal("UpdateUser failed with Validation.Failed",
            host.AdministrationLogger.Collector.GetSnapshot()[1].Message);
        Assert.Equal(LogLevel.Warning, host.AdministrationLogger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task Update_cannot_remove_the_last_active_admin()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("ultimo-admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(admin.Id, "Admin", [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(admin.Id, Ct));
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Create_with_email_invitation_enqueues_it_before_the_commit()
    {
        var host = new UserServiceTestHost();

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(
            "invite@example.com", "Ana", null,
            Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(host.Invitations.Invitations);
        Assert.Single(host.EmailQueue.Messages);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(1, host.QueuedAtCommit);
    }

    [Fact]
    public async Task Create_with_email_invitation_and_a_full_queue_saves_it_as_not_sent_and_logs_it()
    {
        var host = new UserServiceTestHost();
        host.EmailQueue.Accepts = false;

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(
            "invite@example.com", "Ana", null,
            Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct);

        Assert.True(result.IsSuccess);
        Assert.True(Assert.Single(host.Invitations.Invitations).SendFailed);
        Assert.Empty(host.EmailQueue.Messages);
        Assert.Equal(1, host.UnitOfWork.Commits);
        var log = Assert.Single(host.InvitationLogger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, log.Level);
        Assert.DoesNotContain("invite@example.com", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana", log.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_email_invitation_the_full_queue_dropped_does_not_hold_back_the_resend()
    {
        var host = new UserServiceTestHost();
        host.EmailQueue.Accepts = false;
        var created = await host.Administration.CreateUserAsync(new CreateUserRequest(
            "invite@example.com", "Ana", null,
            Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct);
        host.EmailQueue.Accepts = true;

        var resent = await host.Administration.SendInvitationAsync(
            new SendUserInvitationRequest(created.Value.Id, UserInvitationChannel.Email, false), Ct);

        Assert.True(resent.IsSuccess);
        Assert.Single(host.EmailQueue.Messages);
        Assert.Equal([true, false], host.Invitations.Invitations.Select(invitation => invitation.SendFailed));
        Assert.Equal(2, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Create_rejects_an_existing_email_without_committing()
    {
        var host = new UserServiceTestHost();
        host.Accounts.AddUser("taken@example.com");

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest("taken@example.com", "Nueva", null), Ct);

        Assert.Equal(UserErrors.AlreadyExists, result.Error);
        Assert.Single(host.Accounts.Users);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_missing_user_returns_not_found_without_committing()
    {
        var host = new UserServiceTestHost();

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            Guid.CreateVersion7(), "Nadie", [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_missing_user_wins_over_an_unknown_role()
    {
        var host = new UserServiceTestHost();

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            Guid.CreateVersion7(), "Nadie", ["DoesNotExist"]), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_unknown_role_wins_over_a_taken_email()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        host.Accounts.AddUser("taken@example.com");

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            user.Id, "Ana", ["DoesNotExist"], Email: "taken@example.com"), Ct);

        Assert.Equal(RoleErrors.NotFound, result.Error);
        Assert.Equal("ana@example.com", host.Accounts.Users.Single(account => account.Id == user.Id).Email);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_last_admin_wins_over_a_taken_email()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("ultimo-admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);
        host.Accounts.AddUser("taken@example.com");

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            admin.Id, "Admin", [SystemRoles.User], Email: "taken@example.com"), Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(admin.Id, Ct));
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_removing_own_admin_role_wins_over_a_taken_email()
    {
        var host = new UserServiceTestHost();
        var me = host.Accounts.AddUser("me@example.com");
        host.Accounts.SetRoles(me.Id, SystemRoles.Admin);
        var other = host.Accounts.AddUser("other@example.com");
        host.Accounts.SetRoles(other.Id, SystemRoles.Admin);
        host.CurrentUser.UserId = me.Id;
        host.Accounts.AddUser("taken@example.com");

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(
            me.Id, "Yo", [SystemRoles.User], Email: "taken@example.com"), Ct);

        Assert.Equal(UserErrors.CannotModifySelf, result.Error);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(me.Id, Ct));
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_preserves_an_unchanged_phone_that_the_phone_channel_would_reject()
    {
        // La regla del canal (con WhatsApp, el país) vale solo para un número nuevo: el que la cuenta ya tenía no se mira.
        var channel = new FakePhoneChannel(isEnabled: true) { Rejection = UserErrors.PhoneInvalid };
        var host = new UserServiceTestHost(channel);
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: "+59899123456");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana nueva", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", user.PhoneNumber)), Ct);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(host.Accounts.Users);
        Assert.Equal("Ana nueva", updated.DisplayName);
        Assert.Equal(user.PhoneNumber, updated.PhoneNumber);
        Assert.True(updated.PhoneNumberConfirmed);
        Assert.Empty(channel.Checked);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Update_rejects_a_new_phone_that_the_phone_channel_rejects_before_mutation()
    {
        var channel = new FakePhoneChannel(isEnabled: true) { Rejection = UserErrors.PhoneInvalid };
        var host = new UserServiceTestHost(channel);
        var user = host.Accounts.AddUser("ana@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Nuevo", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", "+59899123456")), Ct);

        Assert.Equal(UserErrors.PhoneInvalid, result.Error);
        Assert.Equal("+59899123456", Assert.Single(channel.Checked).Value);
        Assert.Null(Assert.Single(host.Accounts.Users).DisplayName);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_replacing_phone_invalidates_pending_link_after_the_participants_release_the_previous_one()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        var link = LoginLink.Issue(user.Id, "hash", host.Clock.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", [SystemRoles.User],
            Phone: new PhoneNumberInput("AR", "+5493515550202")), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("+5493515550202", Assert.Single(host.Accounts.Users).PhoneNumber);
        Assert.False(Assert.Single(host.Accounts.Users).PhoneNumberConfirmed);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(
            ["participant-lock:" + user.Id, "login-link:" + user.Id, "participant-released:" + user.Id], host.PhoneEvents);
        Assert.Equal("+5493515550202", Assert.Single(host.Participant.LockedPhones)?.Value);
    }

    [Fact]
    public async Task Create_with_email_invitation_locks_the_account_invitations_before_queueing()
    {
        var host = new UserServiceTestHost();
        string[]? eventsWhenQueued = null;
        host.EmailQueue.WhenEnqueued = _ => eventsWhenQueued = [.. host.Invitations.Events];

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(
            "invite@example.com", "Ana", null,
            Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(eventsWhenQueued);
        Assert.Equal(["lock:" + result.Value.Id], eventsWhenQueued);
        Assert.Single(host.EmailQueue.Messages);
        Assert.Single(host.Invitations.Invitations);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    private const string Readers = "Lectores";

    /// <summary>Quien pide pasa a tener sólo «Mesa», que administra cuentas: no tiene Admin ni administra roles.</summary>
    private static void ActAsHelpDesk(UserServiceTestHost host)
    {
        host.RoleReader.RoleNames.Add("Mesa");
        host.RoleReader.Permissions["Mesa"] = [Permissions.Users.Read, Permissions.Users.Manage];
        host.Accounts.SetRoles(host.CurrentUser.UserId!.Value, "Mesa");
    }

    /// <summary>
    /// «Lectores» sólo ve cuentas y «Roles» administra roles. Al asignar, otra transacción ya confirmó dos renombres:
    /// «Lectores» pasó a «Viejo» y «Roles», a «Lectores». SetRolesAsync, que busca por nombre, asigna el segundo.
    /// </summary>
    private static void RenameDuringAssignment(UserServiceTestHost host)
    {
        host.RoleReader.RoleNames.AddRange([Readers, "Roles"]);
        host.RoleReader.Permissions[Readers] = [Permissions.Users.Read];
        host.RoleReader.Permissions["Roles"] = [Permissions.Roles.Read, Permissions.Roles.Manage];
        host.Accounts.OnSetRoles = () =>
        {
            host.RoleReader.Rename(Readers, "Viejo");
            host.RoleReader.Rename("Roles", Readers);
        };
    }
}
