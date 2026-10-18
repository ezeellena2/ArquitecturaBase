using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Time.Testing;
namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFormActionDispatchTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reservation_in_bot_mode_does_not_require_an_operator_or_start_the_message_effect()
    {
        var host = new Host(); host.Repository.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var work = (await host.Service.ReserveAsync(Ct)).Value;
        Assert.NotNull(work);
        Assert.Equal(WhatsAppMessageOrigin.FormAction, work.Origin);
        Assert.NotNull(work.DispatchReservationId);
        Assert.Null(host.Repository.Outgoing.MessageEffectStartedAtUtc);
    }

    [Fact]
    public async Task A_link_revoked_after_reservation_stops_the_effect_before_the_provider()
    {
        var host = new Host(); var work = (await host.Service.ReserveAsync(Ct)).Value!;
        host.Repository.Contact.UnlinkUser();
        var started = await host.Service.MarkFormActionMessageEffectAsync(work.ContactId, work.Id, work.DispatchReservationId!.Value, Ct);
        Assert.False(started.Value);
        Assert.Equal(WhatsAppOutgoingMessageState.Failed, host.Repository.Outgoing.State);
        Assert.Null(host.Repository.Outgoing.MessageEffectStartedAtUtc);
    }

    [Fact]
    public async Task Expired_preparation_requeues_but_an_expired_message_effect_only_reconciles()
    {
        var host = new Host(); var work = (await host.Service.ReserveAsync(Ct)).Value!;
        host.Clock.Advance(TimeSpan.FromMinutes(3)); await host.Service.RecoverExpiredAsync(Ct);
        Assert.Equal(WhatsAppOutgoingMessageState.Pending, host.Repository.Outgoing.State);
        var next = (await host.Service.ReserveAsync(Ct)).Value!;
        Assert.NotEqual(work.DispatchReservationId, next.DispatchReservationId);
        Assert.False((await host.Service.MarkFormActionMessageEffectAsync(work.ContactId, work.Id, work.DispatchReservationId!.Value, Ct)).Value);
        Assert.True((await host.Service.MarkFormActionMessageEffectAsync(next.ContactId, next.Id, next.DispatchReservationId!.Value, Ct)).Value);
        host.Clock.Advance(TimeSpan.FromMinutes(3)); await host.Service.RecoverExpiredAsync(Ct);
        Assert.Equal(WhatsAppOutgoingMessageState.Uncertain, host.Repository.Outgoing.State);
        Assert.Null((await host.Service.ReserveAsync(Ct)).Value);
    }

    [Fact]
    public async Task A_stale_completion_cannot_replace_a_new_preparation()
    {
        var host = new Host(); var old = (await host.Service.ReserveAsync(Ct)).Value!;
        host.Clock.Advance(TimeSpan.FromMinutes(3)); await host.Service.RecoverExpiredAsync(Ct);
        var current = (await host.Service.ReserveAsync(Ct)).Value!;
        var stale = await host.Service.CompleteAsync(old.ContactId, old.Id, "wamid.old", false, Ct, old.DispatchReservationId);
        Assert.True(stale.IsFailure);
        Assert.Equal(WhatsAppOutgoingMessageState.Sending, host.Repository.Outgoing.State);
        Assert.Equal(current.DispatchReservationId, host.Repository.Outgoing.DispatchReservationId);
        Assert.Empty(host.Messages.Added);
    }

    [Fact]
    public async Task A_confirmed_document_keeps_its_system_origin_and_cannot_be_sent_again()
    {
        var host = new Host(true); var work = (await host.Service.ReserveAsync(Ct)).Value!;
        Assert.True((await host.Service.RecordFormActionMediaAsync(work.Id, work.DispatchReservationId!.Value, "123456", Ct)).Value);
        Assert.True((await host.Service.MarkFormActionMessageEffectAsync(work.ContactId, work.Id, work.DispatchReservationId!.Value, Ct)).Value);
        var completed = await host.Service.CompleteAsync(work.ContactId, work.Id, "wamid.confirmed", false, Ct, work.DispatchReservationId);
        Assert.True(completed.IsSuccess);
        var message = Assert.Single(host.Messages.Added);
        Assert.Equal(WhatsAppMessageOrigin.FormAction, message.Origin);
        Assert.Equal(WhatsAppMessageKind.Document, message.Kind);
        Assert.Null(message.OperatorUserId);
        await host.Service.CompleteAsync(work.ContactId, work.Id, "wamid.confirmed", false, Ct, work.DispatchReservationId);
        Assert.Single(host.Messages.Added);
        Assert.Null((await host.Service.ReserveAsync(Ct)).Value);
    }

    private sealed class Host
    {
        public Repository Repository { get; }
        public InMemoryWhatsAppMessageRepository Messages { get; } = new(new LockLog());
        public FakeTimeProvider Clock { get; } = new(Now);
        public WhatsAppChatDispatchService Service { get; }
        public Host(bool document = false)
        {
            var users = new InMemoryUserAccounts(); var user = users.AddUser("recipient@example.com", phoneNumber: "+5493413654813");
            Repository = new(user.Id, document);
            var parser = new FakePhoneNumberParser(); var policy = new WhatsAppFormActionPolicy(users, parser, new FakeWhatsAppAvailability(true, true), Clock);
            Service = new(Repository, Messages, new FakeUnitOfWork(), Clock, new WhatsAppOutgoingSendGuard(parser,new(users,new FakePermissionService()),policy,
                new WhatsAppAssistantContextFixture().Policy,new FakeWhatsAppAssistantReader(),new UnusedSessions(),new FakeAssistantForms(),Clock), NullLogger<WhatsAppChatDispatchService>.Instance);
        }
    }
    private sealed class UnusedSessions : IWhatsAppFlowSessionReader
    {
        public Task<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFormDispatchRow?> FindDispatchAsync(Guid id,CancellationToken ct) => throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFormBindingRow?> FindBindingAsync(Guid definitionId,Guid versionId,string culture,CancellationToken ct) => throw new NotSupportedException();
        public Task<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppFlowReplySessionRow?> FindReplySessionAsync(Guid id,string hash,CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppPendingFlowReplyRow>> ListPendingFlowRepliesAsync(int limit,CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Repository : IWhatsAppConversationRepository
    {
        public WhatsAppContact Contact { get; } = WhatsAppContact.Create("5493413654813", null, "Ana", Now.UtcDateTime);
        public WhatsAppOutgoingMessage Outgoing { get; }
        public Repository(Guid recipient, bool document)
        {
            Contact.LinkUser(recipient);
            Outgoing = WhatsAppOutgoingMessage.CreateFormAction(recipient, Contact.Id, Guid.NewGuid(), "Visita aprobada", Now.UtcDateTime,
                document ? new("private/visit.pdf", "Visit.pdf", "application/pdf", 80, new string('a', 64)) : null, Contact.LinkRevision, Contact.WaId);
        }
        public Task<WhatsAppContact?> GetForUpdateAsync(Guid id, CancellationToken ct) => Task.FromResult<WhatsAppContact?>(Contact.Id == id ? Contact : null);
        public Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid id, CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessage?>(Outgoing.Id == id ? Outgoing : null);
        public Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppAssistantRequest>>([]);
        public Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken ct) => Task.FromResult(false);
        public Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken ct) => Task.FromResult<WhatsAppOutgoingMessage?>(Outgoing.State == WhatsAppOutgoingMessageState.Pending ? Outgoing : null);
        public Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoff, CancellationToken ct) => Task.FromResult<IReadOnlyList<WhatsAppOutgoingMessage>>(Outgoing.State == WhatsAppOutgoingMessageState.Sending && Outgoing.StartedAtUtc < cutoff ? [Outgoing] : []);
        public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contact, Guid actor, Guid request, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ExistsUnresolvedOutgoingAsync(Guid contact, CancellationToken ct) => throw new NotSupportedException();
        public Task<WhatsAppReadState?> GetReadStateAsync(Guid contact, Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public void Add(WhatsAppReadState state) => throw new NotSupportedException();
        public void Add(WhatsAppOutgoingMessage outgoing) => throw new NotSupportedException();
    }
}
