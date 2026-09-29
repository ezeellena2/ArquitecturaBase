using ArquitecturaBase.Application.Models.Users;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Authorization;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services.Users;

/// <summary>
/// El alta y la edición de cuentas con lo real del módulo WhatsApp: el país de un número nuevo (el canal telefónico, que
/// con las opciones de siempre solo manda a AR), el contacto del chat que se suelta al reemplazar el número y el lock de
/// invitaciones que la cola de WhatsApp ve al encolar. Lo mismo sin el módulo lo prueba UserAdministrationServiceTests.
/// </summary>
public sealed class WhatsAppUserAdministrationServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Update_preserves_an_unchanged_phone_from_a_disallowed_country()
    {
        var host = new WhatsAppUserServiceTestHost();
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
        var host = new WhatsAppUserServiceTestHost();
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
    public async Task Update_replacing_phone_invalidates_pending_link_after_unlinking_contact()
    {
        var host = new WhatsAppUserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: "+5493515550101");
        host.Accounts.SetRoles(user.Id, SystemRoles.User);
        var link = LoginLink.Issue(user.Id, "hash", TimeProvider.System.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        var contact = WhatsAppContact.Create("5493515550101", "AR.ana", "Ana", TimeProvider.System.GetUtcNow().UtcDateTime);
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
    public async Task Create_with_whatsapp_invitation_locks_the_account_invitations_before_queueing()
    {
        var host = new WhatsAppUserServiceTestHost();
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
