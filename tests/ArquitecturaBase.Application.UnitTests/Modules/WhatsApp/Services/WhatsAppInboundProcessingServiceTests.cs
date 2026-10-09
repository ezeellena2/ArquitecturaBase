using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppInboundProcessingServiceTests
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));
    private readonly LockLog locks = new();
    private readonly InMemoryWhatsAppContactRepository contacts;
    private readonly InMemoryWhatsAppMessageRepository messages;
    private readonly FakeWhatsAppSendQueue queue = new();
    private readonly FakeUnitOfWork unit = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public WhatsAppInboundProcessingServiceTests()
    {
        contacts = new(locks);
        messages = new(locks);
        locks.InTransaction = () => unit.InTransaction;
    }

    private WhatsAppInboundService Service() => new(contacts, messages, new FakePhoneNumberParser(),
        new WhatsAppAccessReplyPolicy(new FakePublicOrigin(new Uri("https://app.test/")), new FakeSystemSettingsReader()),
        queue, unit, clock, NullLogger<WhatsAppInboundService>.Instance);

    private WhatsAppContact Contact()
    {
        var contact = WhatsAppContact.Create("5493413654813", null, "Ana", Now);
        contacts.Contacts.Add(contact);
        return contact;
    }

    private WhatsAppMessage Add(WhatsAppContact contact, WhatsAppMessageKind kind, string? body, string? id = null, DateTime? at = null)
    {
        var message = WhatsAppMessage.Inbound(contact.Id, "wamid." + Guid.NewGuid(), kind, body, id, at ?? Now);
        messages.Messages.Add(message);
        return message;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Business_messages_leave_account_links_unchanged_and_receive_no_access_reply(bool linked)
    {
        var contact = Contact();
        var accountId = Guid.NewGuid();
        if (linked) { contact.LinkUser(accountId); }
        var message = Add(contact, WhatsAppMessageKind.Text, "Hola");
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Empty(queue.Messages);
        Assert.Equal(linked ? accountId : (Guid?)null, contact.UserId);
        Assert.Equal(WhatsAppMessagePurpose.Business, message.Purpose);
        Assert.Equal(Now, message.ProcessedAtUtc);
        Assert.Equal(1, unit.Commits);
        Assert.Equal(CommitPolicy.OnSuccess, unit.LastPolicy);
    }

    [Theory]
    [InlineData("WANT_TO_ENTER", "login")]
    [InlineData("HAVE_ACCOUNT", "login")]
    [InlineData("CREATE_ACCOUNT", "registro")]
    public async Task Access_buttons_in_manual_mode_receive_only_public_instructions(string action, string path)
    {
        var contact = Contact();
        var message = Add(contact, WhatsAppMessageKind.ButtonReply, "The title does not decide identity", action);
        await Service().ProcessContactAsync(contact.Id, Ct);
        var reply = Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(queue.Messages));
        Assert.Equal("https://app.test/" + path, reply.Url);
        Assert.Null(contact.UserId);
        Assert.Equal(WhatsAppConversationMode.Manual, contact.Mode);
        Assert.Equal(WhatsAppMessagePurpose.Access, message.Purpose);
        Assert.Equal(WhatsAppAccessEventKind.AccessRequested, message.AccessEventKind);
    }

    [Fact]
    public async Task Access_instructions_do_not_depend_on_legacy_business_automation_mode()
    {
        var contact = Contact();
        contact.ChangeMode(WhatsAppConversationMode.Bot);
        Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter);
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Single(queue.Messages);
    }

    [Fact]
    public async Task Newer_business_text_does_not_replace_access_and_reprocessing_does_not_repeat_it()
    {
        var contact = Contact();
        Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter, Now.AddSeconds(-1));
        Add(contact, WhatsAppMessageKind.Text, "Hola");
        await Service().ProcessContactAsync(contact.Id, Ct);
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Single(queue.Messages);
        Assert.All(messages.Messages, message => Assert.Equal(Now, message.ProcessedAtUtc));
    }

    [Fact]
    public async Task A_batch_of_access_actions_receives_only_the_latest_orientation()
    {
        var contact = Contact();
        Add(contact, WhatsAppMessageKind.ButtonReply, "Register", WhatsAppAccessActions.CreateAccount, Now.AddSeconds(-1));
        Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter);
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Equal("https://app.test/login", Assert.IsType<WhatsAppLinkButtonMessage>(Assert.Single(queue.Messages)).Url);
    }

    [Theory]
    [InlineData("DID_NOT_REQUEST_CODE", 0)]
    [InlineData("WANT_TO_ENTER", -24)]
    [InlineData("WANT_TO_ENTER", -25)]
    public async Task Non_answerable_access_actions_are_processed_without_a_reply(string action, int hours)
    {
        var contact = Contact();
        var message = Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", action, Now.AddHours(hours));
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Empty(queue.Messages);
        Assert.NotNull(message.ProcessedAtUtc);
    }

    [Fact]
    public async Task Rejected_access_instructions_roll_back_the_processing_boundary()
    {
        var contact = Contact();
        Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter);
        queue.Accepts = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().ProcessContactAsync(contact.Id, Ct));
        Assert.Equal(1, unit.Rollbacks);
        Assert.Equal(0, unit.Commits);
    }

    [Fact]
    public async Task A_taken_contact_is_skipped_before_reading_its_messages()
    {
        var contact = Contact();
        var message = Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter);
        contacts.LockedElsewhere.Add(contact.Id);
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.Null(message.ProcessedAtUtc);
        Assert.Empty(queue.Messages);
    }

    [Fact]
    public async Task A_contact_without_a_number_is_processed_without_trying_to_send()
    {
        var contact = WhatsAppContact.Create(null, "BSUID.only", "Ana", Now);
        contacts.Contacts.Add(contact);
        var message = Add(contact, WhatsAppMessageKind.ButtonReply, "Enter", WhatsAppAccessActions.WantToEnter);
        await Service().ProcessContactAsync(contact.Id, Ct);
        Assert.NotNull(message.ProcessedAtUtc);
        Assert.Empty(queue.Messages);
    }
}
