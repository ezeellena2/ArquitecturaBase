using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Domain.Users;
using Microsoft.Extensions.Logging;

namespace ArquitecturaBase.Application.UnitTests.Services.Users;

public sealed class UserServiceWriteTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Invalid_create_stops_before_locks_identity_and_commit()
    {
        var host = new UserServiceTestHost();

        var result = await host.Service.CreateUserAsync(new CreateUserRequest("invalid", "Ana", null), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("email"));
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Empty(host.Identity.Users);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling CreateUserCommand", "CreateUserCommand failed with Validation.Failed"],
            host.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_assigns_default_role_and_confirms_once()
    {
        var host = new UserServiceTestHost();
        var email = "alta@example.com";

        var result = await host.Service.CreateUserAsync(new CreateUserRequest(email, "Ana", null), Ct);

        Assert.True(result.IsSuccess);
        var user = Assert.Single(host.Identity.Users);
        Assert.Equal(result.Value, user.Id);
        Assert.Equal(email, user.Email);
        Assert.False(user.EmailConfirmed);
        Assert.Equal([SystemRoles.User], await host.Identity.GetRolesAsync(user.Id, Ct));
        Assert.Equal([email], host.Destinations.LockedDestinations);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
        Assert.Equal(["Handling CreateUserCommand", "Handled CreateUserCommand"],
            host.Logger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Create_locks_email_before_phone_and_rejects_unknown_roles_without_mutation()
    {
        var host = new UserServiceTestHost();
        var request = new CreateUserRequest("ana@example.com", "Ana", ["DoesNotExist"],
            new PhoneNumberInput("AR", "+5493515550101"));

        var result = await host.Service.CreateUserAsync(request, Ct);

        Assert.Equal(RoleErrors.NotFound, result.Error);
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);

        var valid = await host.Service.CreateUserAsync(request with { Roles = [SystemRoles.User] }, Ct);
        Assert.True(valid.IsSuccess);
        Assert.Equal(["ana@example.com", "+5493515550101"], host.Destinations.LockedDestinations);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Create_restores_a_deleted_account_only_when_both_destinations_belong_to_it()
    {
        var host = new UserServiceTestHost();
        var deleted = host.Identity.AddUser("restore@example.com", phoneNumber: "+5493515550101");
        await host.Identity.DeleteAsync(deleted.Id, Ct);
        var request = new CreateUserRequest(deleted.Email, "Nuevo nombre", [SystemRoles.Admin],
            new PhoneNumberInput("AR", deleted.PhoneNumber));

        var result = await host.Service.CreateUserAsync(request, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(deleted.Id, result.Value);
        var restored = Assert.Single(host.Identity.Users);
        Assert.Equal("Nuevo nombre", restored.DisplayName);
        Assert.False(restored.EmailConfirmed);
        Assert.False(restored.PhoneNumberConfirmed);
        Assert.Equal([SystemRoles.Admin], await host.Identity.GetRolesAsync(restored.Id, Ct));
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Create_rejects_destinations_from_two_deleted_accounts_without_commit()
    {
        var host = new UserServiceTestHost();
        var byEmail = host.Identity.AddUser("email@example.com");
        var byPhone = host.Identity.AddUser(null, phoneNumber: "+5493515550101");
        await host.Identity.DeleteAsync(byEmail.Id, Ct);
        await host.Identity.DeleteAsync(byPhone.Id, Ct);

        var result = await host.Service.CreateUserAsync(new CreateUserRequest(
            byEmail.Email, "Ana", null, new PhoneNumberInput("AR", byPhone.PhoneNumber)), Ct);

        Assert.Equal(UserErrors.PhoneAlreadyExists, result.Error);
        Assert.Empty(host.Identity.Users);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Invalid_update_stops_before_locks_and_commit()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("ana@example.com");

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", Roles: null), Ct);

        var error = Assert.IsType<ValidationError>(result.Error);
        Assert.True(error.Errors.ContainsKey("roles"));
        Assert.Empty(host.Destinations.LockedDestinations);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
        Assert.Equal("UpdateUserCommand failed with Validation.Failed",
            host.Logger.Collector.GetSnapshot()[1].Message);
        Assert.Equal(LogLevel.Warning, host.Logger.Collector.GetSnapshot()[1].Level);
    }

    [Fact]
    public async Task Update_preserves_an_unchanged_phone_from_a_disallowed_country()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("ana@example.com", phoneNumber: "+59899123456");
        host.Identity.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana nueva", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", user.PhoneNumber)), Ct);

        Assert.True(result.IsSuccess);
        var updated = Assert.Single(host.Identity.Users);
        Assert.Equal("Ana nueva", updated.DisplayName);
        Assert.Equal(user.PhoneNumber, updated.PhoneNumber);
        Assert.True(updated.PhoneNumberConfirmed);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_rejects_new_phone_from_a_disallowed_country_before_mutation()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("ana@example.com");
        host.Identity.SetRoles(user.Id, SystemRoles.User);

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(user.Id, "Nuevo", [SystemRoles.User],
            Phone: new PhoneNumberInput("UY", "+59899123456")), Ct);

        Assert.Equal(WhatsAppErrors.CountryNotSupported, result.Error);
        Assert.Null(Assert.Single(host.Identity.Users).DisplayName);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_cannot_remove_the_last_active_admin()
    {
        var host = new UserServiceTestHost();
        var admin = host.Identity.AddUser("admin@example.com");
        host.Identity.SetRoles(admin.Id, SystemRoles.Admin);

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(admin.Id, "Admin", [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.LastAdmin, result.Error);
        Assert.Equal([SystemRoles.Admin], await host.Identity.GetRolesAsync(admin.Id, Ct));
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_replacing_phone_invalidates_pending_link_after_unlinking_contact()
    {
        var host = new UserServiceTestHost();
        var user = host.Identity.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Identity.SetRoles(user.Id, SystemRoles.User);
        var link = LoginLink.Issue(user.Id, "hash", TimeProvider.System.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        var contact = ArquitecturaBase.Domain.WhatsApp.WhatsAppContact.Create("5493515550101", "AR.ana", "Ana", TimeProvider.System.GetUtcNow().UtcDateTime);
        contact.LinkUser(user.Id);
        host.Contacts.Contacts.Add(contact);

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(user.Id, "Ana", [SystemRoles.User],
            Phone: new PhoneNumberInput("AR", "+5493515550202")), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal("+5493515550202", Assert.Single(host.Identity.Users).PhoneNumber);
        Assert.False(Assert.Single(host.Identity.Users).PhoneNumberConfirmed);
        Assert.Null(contact.UserId);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
        Assert.True(host.MessagesLog.Events.IndexOf("number-change:" + user.Id) <
            host.MessagesLog.Events.IndexOf("read:GetByUserIdAsync"));
    }
    [Fact]
    public async Task Create_with_email_invitation_enqueues_it_before_the_commit()
    {
        var host = new UserServiceTestHost();

        var result = await host.Service.CreateUserAsync(new CreateUserRequest(
            "invite@example.com", "Ana", null,
            Invitation: new InvitationRequest(UserInvitationChannel.Email, Consent: false)), Ct);

        Assert.True(result.IsSuccess);
        Assert.Single(host.Invitations.Invitations);
        Assert.Single(host.EmailQueue.Messages);
        Assert.Equal(1, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Create_rejects_an_existing_email_without_committing()
    {
        var host = new UserServiceTestHost();
        host.Identity.AddUser("taken@example.com");

        var result = await host.Service.CreateUserAsync(new CreateUserRequest("taken@example.com", "Nueva", null), Ct);

        Assert.Equal(UserErrors.AlreadyExists, result.Error);
        Assert.Single(host.Identity.Users);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }

    [Fact]
    public async Task Update_missing_user_returns_not_found_without_committing()
    {
        var host = new UserServiceTestHost();

        var result = await host.Service.UpdateUserAsync(new UpdateUserRequest(
            Guid.CreateVersion7(), "Nadie", [SystemRoles.User]), Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
        Assert.Equal(0, host.UnitOfWork.SaveChangesCalls);
    }
}
