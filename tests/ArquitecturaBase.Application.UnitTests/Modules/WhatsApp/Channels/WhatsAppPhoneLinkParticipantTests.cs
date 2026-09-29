using ArquitecturaBase.Application.Modules.WhatsApp.Channels;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Channels;

/// <summary>
/// Lo que WhatsApp ata al número de una cuenta es su contacto del chat: el participante delega cada aviso en
/// <see cref="WhatsAppContactLinker"/>, que sigue siendo el único que vincula y suelta contactos.
/// </summary>
public sealed class WhatsAppPhoneLinkParticipantTests
{
    private static readonly PhoneNumber Phone = PhoneNumber.Create("+5493515550101").Value;
    private static readonly DateTime Now = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly LockLog _locks = new();
    private readonly InMemoryWhatsAppContactRepository _contacts;

    public WhatsAppPhoneLinkParticipantTests() => _contacts = new InMemoryWhatsAppContactRepository(_locks);

    [Fact]
    public async Task Locking_takes_the_contact_of_the_account_and_those_of_the_new_number()
    {
        var userId = Guid.CreateVersion7();

        await Participant().LockAsync(userId, Phone, Ct);

        Assert.Equal(["number-change:" + userId, "number-change:wa:5493515550101"], _locks.Keys);
    }

    [Fact]
    public async Task Without_a_new_number_it_locks_only_the_contact_of_the_account()
    {
        var userId = Guid.CreateVersion7();

        await Participant().LockAsync(userId, newPhone: null, Ct);

        Assert.Equal(["number-change:" + userId], _locks.Keys);
    }

    [Fact]
    public async Task A_confirmed_phone_links_the_contact_that_writes_from_it_and_releases_the_previous_one()
    {
        var userId = Guid.CreateVersion7();
        var previous = AddContact("5493515550999", userId);
        var fromTheNumber = AddContact("5493515550101", userId: null);

        await Participant().PhoneConfirmedAsync(userId, Phone, Ct);

        Assert.Equal(userId, fromTheNumber.UserId);
        Assert.Null(previous.UserId);
    }

    [Fact]
    public async Task A_released_phone_releases_the_contact_of_the_account()
    {
        var userId = Guid.CreateVersion7();
        var contact = AddContact("5493515550101", userId);

        await Participant().PhoneReleasedAsync(userId, Ct);

        Assert.Null(contact.UserId);
    }

    private WhatsAppPhoneLinkParticipant Participant() => new(new WhatsAppContactLinker(_contacts));

    private WhatsAppContact AddContact(string waId, Guid? userId)
    {
        var contact = WhatsAppContact.Create(waId, userIdentifier: null, "Ana", Now);
        if (userId is { } id)
        {
            contact.LinkUser(id);
        }

        _contacts.Contacts.Add(contact);
        return contact;
    }
}
