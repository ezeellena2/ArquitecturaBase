using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Users;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// El chat del número de la cuenta, para Mi perfil (tablero PF2): vinculado, con los avisos hasta 24 horas después del
/// último mensaje de la persona, la misma ventana que exige mandarlos; sin vincular, o sin chat. Sólo lee: las guardas de
/// los dobles cortan cualquier escritura o lock, porque no hay transacción.
/// </summary>
public sealed class ProfileWhatsAppChatServiceTests
{
    private const string Phone = "+5493511234567";
    private const string WaId = "5493511234567";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_chat_of_the_confirmed_number_linked_to_the_account_carries_the_notices_until_a_day_after_its_last_message()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        var lastMessage = fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-2);
        fixture.AddContact(WaId, lastMessage).LinkUser(user.Id);

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.Linked, chat.State);
        Assert.Equal(lastMessage.AddHours(24), chat.NoticesUntilUtc);
    }

    [Fact]
    public async Task A_linked_chat_without_a_message_in_the_last_day_carries_no_notices()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        fixture.AddContact(WaId, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-25)).LinkUser(user.Id);

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.Linked, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    /// <summary>
    /// El caso de la prueba completa del 2026-11-03: el número se confirmó antes de que existiera su chat, y el primer
    /// mensaje lo creó suelto. Un mensaje nunca vincula: sólo volver a confirmar el número con el código.
    /// </summary>
    [Fact]
    public async Task The_chat_of_the_number_left_unlinked_is_not_linked_and_carries_no_notices()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        fixture.AddContact(WaId, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-1));

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.NotLinked, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    [Fact]
    public async Task A_number_that_never_wrote_has_no_chat()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.NoChat, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    /// <summary>Como al mandar un aviso: el chat vale si es del número que la cuenta tiene hoy.</summary>
    [Fact]
    public async Task A_chat_still_linked_from_a_previous_number_is_not_the_chat_of_the_account()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com", phoneNumber: Phone);
        fixture.AddContact("5493519999999", fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-1)).LinkUser(user.Id);

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.NoChat, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    [Fact]
    public async Task An_unconfirmed_number_is_never_linked_even_if_its_chat_is()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");
        await fixture.Accounts.ArrangeAsync(accounts =>
            accounts.SetPhoneAsync(user.Id, PhoneNumber.Create(Phone).Value, confirmed: false, Ct));
        fixture.AddContact(WaId, fixture.Clock.GetUtcNow().UtcDateTime.AddHours(-1)).LinkUser(user.Id);

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.NotLinked, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    [Fact]
    public async Task Without_a_number_the_account_has_no_chat()
    {
        var fixture = new Fixture();
        var user = fixture.Accounts.AddUser("ana@example.com");

        var chat = (await fixture.Service(user.Id).GetChatAsync(Ct)).Value;

        Assert.Equal(ProfileWhatsAppChatState.NoChat, chat.State);
        Assert.Null(chat.NoticesUntilUtc);
    }

    [Fact]
    public async Task The_chat_of_an_account_that_no_longer_exists_is_not_found()
    {
        var fixture = new Fixture();

        var result = await fixture.Service(Guid.CreateVersion7()).GetChatAsync(Ct);

        Assert.Equal(UserErrors.NotFound, result.Error);
    }

    private sealed class Fixture
    {
        private readonly LockLog _locks = new() { InTransaction = () => false };

        public Fixture()
        {
            Contacts = new InMemoryWhatsAppContactRepository(_locks);
            Accounts.InTransaction = () => false;
        }

        public InMemoryUserAccounts Accounts { get; } = new();
        public InMemoryWhatsAppContactRepository Contacts { get; }
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 3, 21, 0, 0, TimeSpan.Zero));

        public ProfileWhatsAppChatService Service(Guid userId) =>
            new(new FakeCurrentUser { UserId = userId }, Accounts, new InMemoryWhatsAppContactReader(Contacts), Clock);

        /// <summary>El chat de <paramref name="waId"/>, sin vincular, con el último mensaje de la persona a esa hora.</summary>
        public WhatsAppContact AddContact(string waId, DateTime lastMessageAtUtc)
        {
            var contact = WhatsAppContact.Create(waId, userIdentifier: null, "Ana", lastMessageAtUtc);
            Contacts.Contacts.Add(contact);
            return contact;
        }
    }
}
