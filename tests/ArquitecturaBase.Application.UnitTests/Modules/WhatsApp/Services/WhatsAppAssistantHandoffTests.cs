using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

/// <summary>
/// Derivar (AS4): the conversation goes to Atención manual marked «Necesita una persona», the fixed message goes out although the
/// mode is already Manual, and the mark goes away when the reply of a person goes out.
/// </summary>
public sealed class WhatsAppAssistantHandoffTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 15, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handing_off_marks_the_conversation_and_queues_the_fixed_message_after_the_switch()
    {
        var host = new Host();
        var revision = host.Repository.Contact.ModeRevision;

        var outgoing = await host.Policy.HandOffAsync(host.Repository.Contact, host.Recipient, Guid.CreateVersion7(), "case",
            "Pide hablar con una persona.", " Te paso con una persona del equipo. ", Ct);

        Assert.NotNull(outgoing);
        Assert.Equal(WhatsAppConversationMode.Manual, host.Repository.Contact.Mode);
        Assert.Equal(revision + 1, host.Repository.Contact.ModeRevision);
        Assert.True(host.Repository.Contact.NeedsPerson);
        Assert.True(outgoing.IsAssistantHandoff);
        Assert.Equal("Te paso con una persona del equipo.", outgoing.Body);
        Assert.Equal(host.Repository.Contact.ModeRevision, outgoing.AssistantModeRevision);
        Assert.Same(outgoing, Assert.Single(host.Repository.Added));
    }

    [Fact]
    public async Task Without_the_window_the_mark_stays_and_nothing_is_queued()
    {
        var host = new Host();
        host.Clock.Advance(TimeSpan.FromHours(25));

        var outgoing = await host.Policy.HandOffAsync(host.Repository.Contact, host.Recipient, Guid.CreateVersion7(), "dailyReplyLimit", null, "Te paso.", Ct);

        Assert.Null(outgoing);
        Assert.True(host.Repository.Contact.NeedsPerson);
        Assert.Empty(host.Repository.Added);
    }

    [Fact]
    public async Task The_fixed_message_goes_out_in_manual_and_lands_as_the_assistant()
    {
        var host = new Host();
        await host.Policy.HandOffAsync(host.Repository.Contact, host.Recipient, Guid.CreateVersion7(), "case", "Motivo", "Te paso con una persona.", Ct);

        var work = (await host.Service.ReserveAsync(Ct)).Value.Operation!;
        var started = await host.Service.MarkAssistantMessageEffectAsync(work.ContactId, work.Id, work.DispatchReservationId!.Value, Ct);
        await host.Service.CompleteAsync(work.ContactId, work.Id, "wamid.handoff", false, Ct, work.DispatchReservationId);

        Assert.True(started.Value);
        var message = Assert.Single(host.Messages.Added);
        Assert.Equal(WhatsAppMessageOrigin.Assistant, message.Origin);
        Assert.True(host.Repository.Contact.NeedsPerson);
    }

    [Fact]
    public async Task Going_back_to_the_assistant_before_it_goes_out_leaves_the_fixed_message_unsent()
    {
        var host = new Host();
        await host.Policy.HandOffAsync(host.Repository.Contact, host.Recipient, Guid.CreateVersion7(), "case", "Motivo", "Te paso con una persona.", Ct);
        host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Assistant);

        var work = (await host.Service.ReserveAsync(Ct)).Value.Operation;

        Assert.Null(work);
        Assert.Equal(WhatsAppOutgoingMessageState.Failed, Assert.Single(host.Repository.Added).State);
    }

    [Fact]
    public async Task The_mark_goes_away_when_the_reply_of_a_person_goes_out()
    {
        var host = new Host();
        await host.Policy.HandOffAsync(host.Repository.Contact, host.Recipient, Guid.CreateVersion7(), "case", "Motivo", "Te paso con una persona.", Ct);
        var handoff = (await host.Service.ReserveAsync(Ct)).Value.Operation!;
        await host.Service.MarkAssistantMessageEffectAsync(handoff.ContactId, handoff.Id, handoff.DispatchReservationId!.Value, Ct);
        await host.Service.CompleteAsync(handoff.ContactId, handoff.Id, "wamid.handoff", false, Ct, handoff.DispatchReservationId);
        var reply = WhatsAppOutgoingMessage.Create(Guid.CreateVersion7(), host.Repository.Contact.Id, Guid.CreateVersion7(), "Hola, soy Ana.", Now.UtcDateTime);
        host.Repository.Added.Add(reply);
        reply.Start(Now.UtcDateTime);

        await host.Service.CompleteAsync(reply.ContactId, reply.Id, "wamid.reply", false, Ct);

        Assert.False(host.Repository.Contact.NeedsPerson);
    }

    private sealed class Host
    {
        public Repository Repository { get; }
        public InMemoryWhatsAppMessageRepository Messages { get; } = new(new LockLog());
        public FakeTimeProvider Clock { get; } = new(Now);
        public Guid Recipient { get; }
        public WhatsAppAssistantHandoffPolicy Policy { get; }
        public WhatsAppChatDispatchService Service { get; }

        public Host()
        {
            var users = new InMemoryUserAccounts();
            var user = users.AddUser("recipient@example.com", phoneNumber: "+5493413654813");
            Recipient = user.Id;
            Repository = new(user.Id, Now.UtcDateTime);
            var parser = new FakePhoneNumberParser();
            var recipients = new WhatsAppFormActionPolicy(users, parser, new FakeWhatsAppAvailability(true, true), Clock);
            Policy = new(Repository, recipients, Clock);
            Service = new(Repository, Messages, new FakeUnitOfWork(), Clock, new WhatsAppOutgoingSendGuard(parser, new(users, new FakePermissionService()), recipients,
                new WhatsAppAssistantContextFixture().Policy, new FakeWhatsAppAssistantReader(), null!, new FakeAssistantForms(), Clock),
                NullLogger<WhatsAppChatDispatchService>.Instance);
        }
    }

    private sealed class Repository : IWhatsAppConversationRepository
    {
        public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493413654813", null, "Ana", DateTime.MinValue.ToUniversalTime());
        public List<WhatsAppOutgoingMessage> Added { get; } = [];

        public Repository(Guid recipient, DateTime nowUtc)
        {
            Contact.RecordInbound("5493413654813", null, "Ana", nowUtc);
            Contact.LinkUser(recipient);
            Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        }

        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult<WhatsAppContact?>(Contact.Id == id ? Contact : null);
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id, CancellationToken ct) => Task.FromResult(Added.SingleOrDefault(item => item.Id == id));
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(DateTime waitingSinceUtc, CancellationToken ct) =>
            Task.FromResult(Added.FirstOrDefault(item => item.State == WhatsAppOutgoingMessageState.Pending));
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff, CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppOutgoingMessage>>([]);
        public Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppAssistantRequest>>([]);
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken ct) => Task.FromResult(false);
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contact, Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid contact, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid contact, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing) => Added.Add(outgoing);
    }
}
