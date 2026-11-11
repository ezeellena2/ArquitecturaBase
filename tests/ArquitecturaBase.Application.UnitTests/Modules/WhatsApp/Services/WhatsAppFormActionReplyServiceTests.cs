using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.ValueObjects;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFormActionReplyServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private const string WaId = "5493413654813";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tap_on_a_sent_button_reaches_every_owner_once_outside_any_transaction_and_is_marked()
    {
        var host = new Host();
        var tap = host.Tap(host.ReplyId("issue"));

        Assert.True((await host.Service.ProcessAsync(host.ContactId, Ct)).IsSuccess);

        Assert.Equal([new Call(host.RunId, host.User.Id, "issue", false)], host.First.Calls);
        Assert.Equal(host.First.Calls, host.Second.Calls);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
        Assert.Empty(host.Actions.Outgoing);
        Assert.Equal(1, host.UnitOfWork.Transactions);
        Assert.True((await host.Service.ProcessAsync(host.ContactId, Ct)).IsSuccess);
        Assert.Single(host.First.Calls);
        Assert.Equal(1, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task The_text_an_owner_hands_back_with_an_applied_tap_goes_to_the_chat_once_by_the_normal_outgoing()
    {
        var host = new Host();
        host.First.Text = "No la emitimos. FAC-000012 quedó anulada.";
        var tap = host.Tap(host.ReplyId("skip"));
        host.Tap(host.ReplyId("issue"));

        await host.Service.ProcessAsync(host.ContactId, Ct);

        // The later tap reaches the owner too, which ignores it: only the applied one is answered, in the transaction that marks the taps.
        var answer = Assert.Single(host.Actions.Outgoing);
        Assert.Equal((WhatsAppMessageOrigin.FormAction, WhatsAppOutgoingMessageKind.Text, WhatsAppOutgoingMessageState.Pending, "No la emitimos. FAC-000012 quedó anulada."),
            (answer.Origin, answer.Kind, answer.State, answer.Body));
        Assert.Equal((tap.Id, host.User.Id, host.ContactId, host.Contact.LinkRevision, WaId),
            (answer.RequestId, answer.RecipientUserId!.Value, answer.ContactId, answer.RecipientLinkRevision!.Value, answer.RecipientWaId));
        Assert.All(host.First.Calls, call => Assert.False(call.InTransaction));
        Assert.All(host.Messages.Messages, message => Assert.NotNull(message.AssistantProcessedAtUtc));
        Assert.Equal(1, host.UnitOfWork.Transactions);

        host.Tap(host.ReplyId("skip"));
        await host.Service.ProcessAsync(host.ContactId, Ct);

        Assert.Single(host.Actions.Outgoing);
    }

    [Fact]
    public async Task Without_the_reply_window_the_text_is_not_answered_and_the_tap_is_marked()
    {
        var host = new Host();
        host.First.Text = "No la emitimos. FAC-000012 quedó anulada.";
        var tap = host.Tap(host.ReplyId("skip"));
        host.Clock.Advance(TimeSpan.FromHours(25));

        Assert.True((await host.Service.ProcessAsync(host.ContactId, Ct)).IsSuccess);

        Assert.Equal([StructuredRecordActionReplyState.Applied], host.First.Answers);
        Assert.Empty(host.Actions.Outgoing);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task Two_taps_on_the_same_message_reach_the_owner_oldest_first_and_the_owner_ignores_the_repeat()
    {
        var host = new Host();
        host.Tap(host.ReplyId("issue"));
        host.Tap(host.ReplyId("skip"));

        await host.Service.ProcessAsync(host.ContactId, Ct);

        Assert.Equal(["issue", "skip"], host.First.Calls.Select(call => call.ChoiceId));
        Assert.Equal([StructuredRecordActionReplyState.Applied, StructuredRecordActionReplyState.Ignored], host.First.Answers);
        Assert.All(host.Messages.Messages, message => Assert.NotNull(message.AssistantProcessedAtUtc));
    }

    [Fact]
    public async Task An_unknown_or_stale_button_or_another_contact_reaches_no_owner_but_is_marked()
    {
        var host = new Host();
        var other = Guid.NewGuid();
        WhatsAppMessage[] taps =
        [
            host.Tap(WhatsAppFormActionChoice.ReplyId(Guid.CreateVersion7(), "issue")),
            host.Tap(host.ReplyId("other")),
            host.Tap(WhatsAppFormActionChoice.ReplyPrefix + "not-an-id"),
            host.Tap(host.ReplyId("issue"), linkRevision: 2),
            host.Tap(host.ReplyId("issue"), contactId: other, userId: Guid.NewGuid()),
            host.Tap(host.ReplyId("issue"), contactId: other),
        ];

        await host.Service.ProcessAsync(host.ContactId, Ct);
        await host.Service.ProcessAsync(other, Ct);

        Assert.Empty(host.First.Calls);
        Assert.All(taps, tap => Assert.NotNull(tap.AssistantProcessedAtUtc));
    }

    [Fact]
    public async Task A_recipient_whose_number_is_no_longer_confirmed_is_ignored()
    {
        var host = new Host();
        await host.Users.SetPhoneAsync(host.User.Id, PhoneNumber.Create("+" + WaId).Value, false, Ct);
        var tap = host.Tap(host.ReplyId("issue"));

        await host.Service.ProcessAsync(host.ContactId, Ct);

        Assert.Empty(host.First.Calls);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    [Fact]
    public async Task An_owner_failure_leaves_the_taps_unmarked_and_the_next_round_hands_them_over_again()
    {
        var host = new Host();
        var tap = host.Tap(host.ReplyId("issue"));
        host.First.FailNext = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Service.ProcessAsync(host.ContactId, Ct));
        Assert.Null(tap.AssistantProcessedAtUtc);
        Assert.Equal(0, host.UnitOfWork.Transactions);

        await host.Service.ProcessAsync(host.ContactId, Ct);

        Assert.Equal(2, host.First.Calls.Count);
        Assert.Single(host.Second.Calls);
        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    // Every round reads its taps' buttons once and marks its taps with one more read, however many taps it has.
    [Fact]
    public async Task A_round_reads_the_buttons_of_all_its_taps_at_once_and_marks_them_with_one_read()
    {
        var host = new Host();
        WhatsAppMessage[] taps =
        [
            host.Tap(host.ReplyId("issue")),
            host.Tap(host.ReplyId("skip")),
            host.Tap(WhatsAppFormActionChoice.ReplyId(Guid.CreateVersion7(), "issue")),
            host.Tap(WhatsAppFormActionChoice.ReplyPrefix + "not-an-id"),
        ];

        Assert.True((await host.Service.ProcessAsync(host.ContactId, Ct)).IsSuccess);

        Assert.Equal(["issue", "skip"], host.First.Calls.Select(call => call.ChoiceId));
        Assert.Equal((1, 1), (host.Reader.TargetReads, host.Actions.TapReads));
        Assert.All(taps, tap => Assert.NotNull(tap.AssistantProcessedAtUtc));
    }

    [Fact]
    public async Task Without_owners_a_tap_is_only_marked()
    {
        var host = new Host(withOwners: false);
        var tap = host.Tap(host.ReplyId("issue"));

        Assert.True((await host.Service.ProcessAsync(host.ContactId, Ct)).IsSuccess);

        Assert.NotNull(tap.AssistantProcessedAtUtc);
    }

    private sealed record Call(Guid RunId, Guid RecipientUserId, string ChoiceId, bool InTransaction);

    private sealed class Host
    {
        public InMemoryUserAccounts Users { get; } = new();
        public InMemoryWhatsAppMessageRepository Messages { get; } = new(new LockLog());
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Now);
        public ActionRepository Actions { get; }
        public Reader Reader { get; }
        public Owner First { get; }
        public Owner Second { get; }
        public UserAccount User { get; }
        public WhatsAppContact Contact { get; } = WhatsAppContact.Create(WaId, null, "Ana", Now.UtcDateTime);
        public Guid ContactId => Contact.Id;
        public Guid RunId { get; } = Guid.NewGuid();
        public WhatsAppOutgoingMessage Outgoing { get; }
        public WhatsAppFormActionReplyService Service { get; }

        public Host(bool withOwners = true)
        {
            User = Users.AddUser("recipient@example.com", phoneNumber: "+" + WaId);
            Contact.LinkUser(User.Id);
            Actions = new ActionRepository(Messages) { Contact = Contact };
            Outgoing = WhatsAppOutgoingMessage.CreateFormAction(User.Id, ContactId, RunId, "¿La emito?", Now.UtcDateTime, null, 1, WaId,
                [new("issue", "Emitir"), new("skip", "No emitir")]);
            Reader = new Reader(Messages);
            Reader.Targets[Outgoing.Id] = new(Outgoing.Id, Outgoing.ContactId, Outgoing.RequestId, User.Id, Outgoing.RecipientLinkRevision,
                Outgoing.RecipientWaId, Outgoing.QuickRepliesJson!);
            First = new Owner(UnitOfWork);
            Second = new Owner(UnitOfWork);
            var policy = new WhatsAppFormActionReplyPolicy(new WhatsAppFormActionPolicy(Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true), Clock));
            Service = new(Reader, Actions, UnitOfWork, policy, withOwners ? [First, Second] : [], Clock, NullLogger<WhatsAppFormActionReplyService>.Instance);
        }

        public string ReplyId(string choiceId) => WhatsAppFormActionChoice.ReplyId(Outgoing.Id, choiceId);

        public WhatsAppMessage Tap(string replyId, Guid? contactId = null, Guid? userId = null, long linkRevision = 1)
        {
            var contact = contactId ?? ContactId;
            var message = WhatsAppMessage.Inbound(contact, "wamid.tap-" + Guid.NewGuid().ToString("N"), WhatsAppMessageKind.ButtonReply, "Emitir", replyId, Clock.GetUtcNow().UtcDateTime);
            message.MarkProcessed(Clock.GetUtcNow().UtcDateTime);
            Messages.Messages.Add(message);
            Reader.Taps.Add(new(message.Id, message.WaMessageId, contact, replyId, userId ?? User.Id, linkRevision, WaId));
            Clock.Advance(TimeSpan.FromSeconds(1));
            return message;
        }
    }

    private sealed class Owner(FakeUnitOfWork unitOfWork) : IStructuredRecordActionReplySink
    {
        private readonly HashSet<(Guid, Guid)> decided = [];
        public List<Call> Calls { get; } = [];
        public List<StructuredRecordActionReplyState> Answers { get; } = [];
        public bool FailNext { get; set; }
        public string? Text { get; set; }

        public Task<StructuredRecordActionReply> ReplyAsync(Guid runId, Guid recipientUserId, string choiceId, CancellationToken cancellationToken)
        {
            Calls.Add(new(runId, recipientUserId, choiceId, unitOfWork.InTransaction));
            if (FailNext) { FailNext = false; throw new InvalidOperationException("The owner is unavailable."); }
            var answer = decided.Add((runId, recipientUserId))
                ? new StructuredRecordActionReply(StructuredRecordActionReplyState.Applied, Text) : new StructuredRecordActionReply(StructuredRecordActionReplyState.Ignored);
            Answers.Add(answer.State);
            return Task.FromResult(answer);
        }
    }

    private sealed class Reader(InMemoryWhatsAppMessageRepository messages) : IWhatsAppFormActionReader
    {
        public List<WhatsAppFormActionTapRow> Taps { get; } = [];
        public Dictionary<Guid, WhatsAppFormActionReplyTargetRow> Targets { get; } = [];
        public int TargetReads { get; private set; }

        public Task<IReadOnlyList<WhatsAppFormActionTapRow>> ListPendingRepliesAsync(Guid contactId, DateTime nowUtc, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<WhatsAppFormActionTapRow>>([.. Taps.Where(tap => tap.ContactId == contactId
                && messages.Messages.Single(message => message.Id == tap.Id).AssistantProcessedAtUtc is null).Take(limit)]);

        public Task<IReadOnlyList<WhatsAppFormActionReplyTargetRow>> ListReplyTargetsAsync(IReadOnlyCollection<Guid> outgoingIds, CancellationToken ct)
        {
            TargetReads++;
            return Task.FromResult<IReadOnlyList<WhatsAppFormActionReplyTargetRow>>([.. outgoingIds.Where(Targets.ContainsKey).Select(id => Targets[id])]);
        }

        public Task<IReadOnlyList<Guid>> ListContactsWithPendingRepliesAsync(IReadOnlyCollection<Guid> excluded, int limit, DateTime nowUtc, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<string?> FindLinkedWaIdAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class ActionRepository(InMemoryWhatsAppMessageRepository messages) : IWhatsAppFormActionRepository
    {
        public WhatsAppContact? Contact { get; set; }
        public List<WhatsAppOutgoingMessage> Outgoing { get; } = [];
        public int TapReads { get; private set; }
        public Task<IReadOnlyList<WhatsAppMessage>> ListTapsAsync(Guid contactId, IReadOnlyCollection<Guid> tapIds, CancellationToken ct)
        {
            TapReads++;
            return Task.FromResult<IReadOnlyList<WhatsAppMessage>>([.. messages.Messages.Where(message => tapIds.Contains(message.Id)
                && message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound)]);
        }
        public Task<WhatsAppContact?> GetContactForUpdateAsync(Guid recipientUserId, CancellationToken ct) =>
            Task.FromResult(Contact?.UserId == recipientUserId ? Contact : null);
        public void Add(WhatsAppOutgoingMessage outgoing) => Outgoing.Add(outgoing);
        public Task LockAsync(Guid runId, Guid recipientUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRunAsync(Guid runId, Guid recipientUserId, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppOutgoingMessage?> GetByRunForUpdateAsync(Guid runId, Guid recipientUserId, CancellationToken ct) => throw new NotSupportedException();
    }
}
