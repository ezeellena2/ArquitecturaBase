using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.WhatsApp;
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
        Assert.Equal(result.Value, user.Id);
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
        Assert.Equal(deleted.Id, result.Value);
        var restored = Assert.Single(host.Accounts.Users);
        Assert.Equal("Nuevo nombre", restored.DisplayName);
        Assert.False(restored.EmailConfirmed);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(restored.Id, Ct));
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
    public async Task Update_preserves_an_unchanged_phone_from_a_disallowed_country()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: "+59899123456");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana nueva", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", user.PhoneNumber)), Ct);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(host.Accounts.Users);
        Assert.Equal("Ana nueva", updated.DisplayName);
        Assert.Equal(user.PhoneNumber, updated.PhoneNumber);
        Assert.True(updated.PhoneNumberConfirmed);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    [Fact]
    public async Task Update_rejects_new_phone_from_a_disallowed_country_before_mutation()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Nuevo", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", "+59899123456")), Ct);

        Assert.Equal(WhatsAppErrors.CountryNotSupported, result.Error);
        Assert.Null(Assert.Single(host.Accounts.Users).DisplayName);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_cannot_remove_the_last_active_admin()
    {
        var host = new UserServiceTestHost();
        var admin = host.Accounts.AddUser("admin@example.com");
        host.Accounts.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(admin.Id, "Admin", [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.Equal([SystemRoles.Admin], await host.Accounts.ListRoleNamesForUserAsync(admin.Id, Ct));
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Update_replacing_phone_invalidates_pending_link_after_unlinking_contact()
    {
        var host = new UserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        var link = LoginLink.Issue(user.Id, "hash", TimeProvider.System.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        var contact = ArquitecturaBase.Domain.WhatsApp.WhatsAppContact.Create("5493515550101", "AR.ana", "Ana", TimeProvider.System.GetUtcNow().UtcDateTime);
        contact.LinkUser(user.Id);
        host.Contacts.Contacts.Add(contact);

        var result = await host.Administration.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", [SystemRoles.User],
            Phone: new PhoneNumberInput("AR", "+5493515550202")), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("+5493515550202", Assert.Single(host.Accounts.Users).PhoneNumber);
        Assert.False(Assert.Single(host.Accounts.Users).PhoneNumberConfirmed);
        Assert.Null(contact.UserId);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.True(host.MessagesLog.Events.IndexOf("number-change:" + user.Id) <
            host.MessagesLog.Events.IndexOf("read:GetByUserIdAsync"));
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
            new SendUserInvitationRequest(created.Value, UserInvitationChannel.Email, false), Ct);

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
        var admin = host.Accounts.AddUser("admin@example.com");
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
    public async Task Create_with_whatsapp_invitation_locks_the_account_invitations_before_queueing()
    {
        var host = new UserServiceTestHost();
        string[]? eventsWhenQueued = null;
        host.SendQueue.WhenEnqueued = _ => eventsWhenQueued = [.. host.Invitations.Events];

        var result = await host.Administration.CreateUserAsync(new CreateUserRequest(
            null, "Ana", null, new PhoneNumberInput("AR", "+5493515550101"),
            new InvitationRequest(UserInvitationChannel.WhatsApp, Consent: true)), Ct);

        Assert.True(result.IsSuccess);
        Assert.NotNull(eventsWhenQueued);
        Assert.Equal(["lock:" + result.Value], eventsWhenQueued);
        Assert.Single(host.SendQueue.Messages);
        Assert.Single(host.Invitations.Invitations);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }
}
