using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Domain.Authentication;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>El participante del chat se suelta también cuando desvincula el servicio del núcleo del perfil.</summary>
public sealed class WhatsAppProfilePhoneServiceTests
{
    private const string Phone = "+5493511234567";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Refusing_to_unlink_the_only_login_method_keeps_the_contact_and_links()
    {
        var host = new WhatsAppUserServiceTestHost();
        var user = host.Accounts.AddUser(email: null, phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var contact = AddContact(host, user.Id);
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.Equal(UserErrors.LastLoginMethod, result.Error);
        Assert.Equal(user.Id, contact.UserId);
        Assert.Null(link.InvalidatedAtUtc);
        Assert.Equal(0, host.UnitOfWork.Commits);
        Assert.Equal(1, host.UnitOfWork.Rollbacks);
    }

    [Fact]
    public async Task Unlink_locks_the_contacts_then_the_account_links_and_voids_only_the_active_links()
    {
        var host = new WhatsAppUserServiceTestHost();
        var user = host.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        host.CurrentUser.UserId = user.Id;
        var contact = AddContact(host, user.Id);
        var active = AddLink(host, user.Id);
        var expired = LoginLink.Issue(user.Id, "hash-expired", host.Clock.GetUtcNow().UtcDateTime.AddHours(-1));
        host.Links.Links.Add(expired);
        host.Links.WhileWaitingForTheLock = userId =>
        {
            host.MessagesLog.Lock(["login-link:" + userId]);
            return Task.CompletedTask;
        };

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["number-change:" + user.Id, "login-link:" + user.Id], host.MessagesLog.Keys);
        var updated = await host.Accounts.FindByIdAsync(user.Id, Ct);
        Assert.Null(updated!.PhoneNumber);
        Assert.False(updated.PhoneNumberConfirmed);
        Assert.Null(contact.UserId);
        Assert.NotNull(active.InvalidatedAtUtc);
        Assert.Null(expired.InvalidatedAtUtc);
        Assert.Equal(1, host.UnitOfWork.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, host.UnitOfWork.LastPolicy);
        Assert.Equal(["Handling UnlinkOwnPhone", "Handled UnlinkOwnPhone"],
            host.ProfilePhoneLogger.Collector.GetSnapshot().Select(record => record.Message));
    }

    [Fact]
    public async Task Unlink_without_a_number_still_releases_the_contact_and_voids_the_links()
    {
        var host = new WhatsAppUserServiceTestHost();
        var user = host.Accounts.AddUser(email: null);
        host.CurrentUser.UserId = user.Id;
        var contact = AddContact(host, user.Id);
        var link = AddLink(host, user.Id);

        var result = await host.ProfilePhone.UnlinkOwnPhoneAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(contact.UserId);
        Assert.NotNull(link.InvalidatedAtUtc);
        Assert.Equal([user.Id], host.Links.LockedAccounts);
        Assert.Equal(1, host.UnitOfWork.Commits);
    }

    private static WhatsAppContact AddContact(WhatsAppUserServiceTestHost host, Guid userId)
    {
        var contact = WhatsAppContact.Create("5493511234567", userIdentifier: null, "Ana", host.Clock.GetUtcNow().UtcDateTime);
        contact.LinkUser(userId);
        host.Contacts.Contacts.Add(contact);
        return contact;
    }

    private static LoginLink AddLink(WhatsAppUserServiceTestHost host, Guid userId)
    {
        var link = LoginLink.Issue(userId, "hash", host.Clock.GetUtcNow().UtcDateTime);
        host.Links.Links.Add(link);
        return link;
    }
}
