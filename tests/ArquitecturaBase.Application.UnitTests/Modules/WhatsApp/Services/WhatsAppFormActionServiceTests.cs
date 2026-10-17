using ArquitecturaBase.Domain.ValueObjects;
using ArquitecturaBase.Application.Models.Identity;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppFormActionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_system_action_is_accepted_in_bot_mode_without_operator_permissions()
    {
        var host = new Host(); host.Repository.Contact!.ChangeMode(WhatsAppConversationMode.Bot);
        var result = await host.Service.EnqueueAsync(host.Message, Ct);
        Assert.Equal(StructuredRecordActionState.Pending, result.State);
        var outgoing = Assert.Single(host.Repository.Outgoing);
        Assert.Null(outgoing.OperatorUserId);
        Assert.Equal(host.User.Id, outgoing.RecipientUserId);
        Assert.Equal(host.Message.RunId, outgoing.RequestId);
        Assert.Equal(host.Repository.Contact.LinkRevision, outgoing.RecipientLinkRevision);
        Assert.Equal(1, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task A_replay_finds_the_original_operation_before_resolving_a_changed_phone_or_contact()
    {
        var host = new Host(); await host.Service.EnqueueAsync(host.Message, Ct);
        host.Repository.Contact = null;
        var repeated = await host.Service.EnqueueAsync(host.Message, Ct);
        Assert.Equal(StructuredRecordActionState.Pending, repeated.State);
        Assert.Single(host.Repository.Outgoing);
        Assert.Equal(1, host.Repository.ContactReads);
    }

    [Fact]
    public async Task A_replay_cannot_replace_the_frozen_body()
    {
        var host = new Host(); await host.Service.EnqueueAsync(host.Message, Ct);
        var result = await host.Service.EnqueueAsync(host.Message with { Body = "Otro texto" }, Ct);
        Assert.Equal(StructuredRecordActionState.Failed, result.State);
        Assert.Equal(WhatsAppFormActionErrors.StateConflict.Code, result.FailureCode);
        Assert.False(result.CanRetry);
        Assert.Equal(host.Message.Body, Assert.Single(host.Repository.Outgoing).Body);
    }

    [Fact]
    public async Task A_closed_window_fails_without_creating_a_send()
    {
        var host = new Host(); host.Clock.Advance(TimeSpan.FromHours(24));
        var result = await host.Service.EnqueueAsync(host.Message, Ct);
        Assert.Equal(StructuredRecordActionState.Failed, result.State);
        Assert.Equal(WhatsAppFormActionErrors.WindowClosed.Code, result.FailureCode);
        Assert.True(result.CanRetry);
        Assert.Empty(host.Repository.Outgoing);
    }

    [Fact]
    public async Task An_inactive_account_cannot_receive_a_system_action()
    {
        var host = new Host(false);
        var result = await host.Service.EnqueueAsync(host.Message, Ct);
        Assert.Equal(WhatsAppFormActionErrors.RecipientUnavailable.Code, result.FailureCode);
        Assert.Empty(host.Repository.Outgoing);
    }

    [Fact]
    public async Task An_unconfirmed_phone_never_queues_a_system_message()
    {
        var host = new Host();
        await host.Users.SetPhoneAsync(host.User.Id, PhoneNumber.Create("+5493413654813").Value, false, Ct);
        var result = await host.Service.EnqueueAsync(host.Message, Ct);
        Assert.Equal(WhatsAppFormActionErrors.RecipientUnavailable.Code, result.FailureCode);
        Assert.Empty(host.Repository.Outgoing);
    }

    [Fact]
    public async Task A_document_caption_must_fit_the_provider_limit()
    {
        var host = new Host();
        var message = host.Message with { Body = new string('x', 1025), Document = new("private/action.pdf", "Visit.pdf", "application/pdf", 80, new string('a', 64)) };
        var result = await host.Service.EnqueueAsync(message, Ct);
        Assert.Equal(WhatsAppFormActionErrors.InvalidMessage.Code, result.FailureCode);
        Assert.Empty(host.Repository.Outgoing);
        Assert.Equal(0, host.UnitOfWork.Transactions);
    }

    [Fact]
    public async Task Querying_uncertainty_never_reopens_the_message_and_retry_cannot_repeat_acceptance()
    {
        var host = new Host(); await host.Service.EnqueueAsync(host.Message, Ct);
        var outgoing = Assert.Single(host.Repository.Outgoing); var reservation = outgoing.ReserveFormAction(Now.UtcDateTime);
        Assert.True(outgoing.MarkFormActionMessageEffect(reservation, Now.UtcDateTime)); outgoing.MarkUncertain();
        Assert.Equal(StructuredRecordActionState.Uncertain, (await host.Service.GetAsync(host.Message.RunId, host.User.Id, Ct)).State);
        Assert.False((await host.Service.RetryAsync(host.Message.RunId, host.User.Id, 2, Ct)).CanRetry);
        outgoing.Accept("wamid.confirmed");
        Assert.Equal(StructuredRecordActionState.Accepted, (await host.Service.RetryAsync(host.Message.RunId, host.User.Id, 2, Ct)).State);
        Assert.Single(host.Repository.Outgoing);
    }

    [Fact]
    public async Task Retrying_a_failure_revalidates_the_fixed_link_and_never_switches_destination()
    {
        var host = new Host(); await host.Service.EnqueueAsync(host.Message, Ct);
        var outgoing = Assert.Single(host.Repository.Outgoing); outgoing.Fail(WhatsAppFormActionErrors.SendRejected.Code);
        host.Repository.Contact!.UnlinkUser(); host.Repository.Contact.LinkUser(host.User.Id);
        var result = await host.Service.RetryAsync(host.Message.RunId, host.User.Id, 2, Ct);
        Assert.Equal(StructuredRecordActionState.Failed, result.State);
        Assert.Equal(WhatsAppFormActionErrors.RecipientChanged.Code, result.FailureCode);
        Assert.Single(host.Repository.Outgoing);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_human_review_changes_only_the_uncertain_outbox_and_keeps_its_original_attribution(bool wasSent)
    {
        var host=new Host();await host.Service.EnqueueAsync(host.Message,Ct);var outgoing=Assert.Single(host.Repository.Outgoing);
        var reservation=outgoing.ReserveFormAction(Now.UtcDateTime);outgoing.MarkFormActionMessageEffect(reservation,Now.UtcDateTime);outgoing.MarkUncertain();
        var evidence=new StructuredRecordActionReview(1,wasSent,Guid.NewGuid(),Now.AddMinutes(1).UtcDateTime);
        var reviewed=await host.Service.ReviewAsync(host.Message.RunId,host.User.Id,evidence,Ct);
        Assert.Equal(wasSent?StructuredRecordActionState.Accepted:StructuredRecordActionState.Failed,reviewed.State);
        Assert.Equal(evidence,reviewed.Review);Assert.Null(reviewed.Receipt);
        Assert.Equal(!wasSent,reviewed.CanRetry);Assert.Equal(evidence.ReviewedByUserId,outgoing.DeliveryReviewedByUserId);
        Assert.Equal(evidence.ReviewedAtUtc,outgoing.DeliveryReviewedAtUtc);Assert.Null(outgoing.WaMessageId);Assert.Single(host.Repository.Outgoing);
        var repeat=await host.Service.ReviewAsync(host.Message.RunId,host.User.Id,evidence with{WasSent=!wasSent},Ct);
        Assert.Equal(reviewed,repeat);Assert.Equal(wasSent,outgoing.DeliveryReviewWasSent);
    }
    [Fact]
    public async Task An_acceptance_observed_under_the_review_lock_cannot_be_overwritten_by_a_not_sent_decision()
    {
        var host=new Host();await host.Service.EnqueueAsync(host.Message,Ct);var outgoing=Assert.Single(host.Repository.Outgoing);
        var reservation=outgoing.ReserveFormAction(Now.UtcDateTime);outgoing.MarkFormActionMessageEffect(reservation,Now.UtcDateTime);outgoing.MarkUncertain();
        host.Repository.BeforeReviewLock=()=>outgoing.Accept("wamid.concurrent");
        var status=await host.Service.ReviewAsync(host.Message.RunId,host.User.Id,new(1,false,Guid.NewGuid(),Now.UtcDateTime),Ct);
        Assert.Equal(StructuredRecordActionState.Accepted,status.State);Assert.Null(outgoing.DeliveryReviewWasSent);Assert.Equal("wamid.concurrent",outgoing.WaMessageId);
    }
    [Fact]
    public async Task Local_observation_reads_one_batch_and_never_opens_a_write_boundary()
    {
        var host=new Host();var readAt=Now.UtcDateTime;
        host.Reader.Rows.Add(new(host.Message.RunId,host.User.Id,WhatsAppOutgoingMessageState.Accepted,null,true,0,null,null,null,null,readAt));
        var result=await host.Service.ObserveAsync([new(host.Message.RunId,host.User.Id)],Ct);
        var observed=Assert.Single(result);Assert.Equal(StructuredRecordActionState.Accepted,observed.Status.State);
        Assert.Null(observed.Status.Receipt!.DeliveredAtUtc);Assert.Equal(readAt,observed.Status.Receipt.ReadAtUtc);
        Assert.Equal(1,host.Reader.Calls);Assert.Equal(0,host.UnitOfWork.Transactions);Assert.Empty(host.Repository.Outgoing);Assert.Equal(0,host.Repository.ContactReads);
    }
    private sealed class Host
    {
        public Repository Repository { get; } = new();
        public Reader Reader { get; }=new();
        public InMemoryUserAccounts Users { get; } = new();
        public FakeUnitOfWork UnitOfWork { get; } = new();
        public FakeTimeProvider Clock { get; } = new(Now);
        public UserAccount User { get; }
        public StructuredRecordActionMessage Message { get; }
        public WhatsAppFormActionService Service { get; }
        public Host(bool active = true)
        {
            User = Users.AddUser("recipient@example.com", active, phoneNumber: "+5493413654813");
            Repository.Contact!.LinkUser(User.Id);
            Message = new(Guid.NewGuid(), User.Id, "La visita VIS-000001 fue aprobada.");
            var policy = new WhatsAppFormActionPolicy(Users, new FakePhoneNumberParser(), new FakeWhatsAppAvailability(true, true), Clock);
            Service = new(Repository,Reader,UnitOfWork,policy,Clock, NullLogger<WhatsAppFormActionService>.Instance);
        }
    }
    private sealed class Reader:IWhatsAppFormActionReader
    {
        public List<WhatsAppFormActionObservationRow> Rows { get; }=[];public int Calls { get; private set; }
        public Task<IReadOnlyList<WhatsAppFormActionObservationRow>> ListObservationsAsync(IReadOnlyCollection<StructuredRecordActionObservationRequest> requests,CancellationToken ct)
        { Calls++;return Task.FromResult<IReadOnlyList<WhatsAppFormActionObservationRow>>(Rows.Where(row=>requests.Contains(new(row.RunId,row.RecipientUserId))).ToArray()); }
    }
    private sealed class Repository : IWhatsAppFormActionRepository
    {
        public WhatsAppContact? Contact { get; set; } = WhatsAppContact.Create("5493413654813", null, "Ana", Now.UtcDateTime);
        public List<WhatsAppOutgoingMessage> Outgoing { get; } = [];
        public int ContactReads { get; private set; }
        public Task LockAsync(Guid run, Guid recipient, CancellationToken ct) => Task.CompletedTask;
        public Task<WhatsAppOutgoingMessage?> GetByRunAsync(Guid run, Guid recipient, CancellationToken ct) => Task.FromResult(Outgoing.SingleOrDefault(item => item.RequestId == run && item.RecipientUserId == recipient));
        public Action? BeforeReviewLock { get; set; }
        public Task<WhatsAppOutgoingMessage?> GetByRunForUpdateAsync(Guid run, Guid recipient, CancellationToken ct) { BeforeReviewLock?.Invoke(); return GetByRunAsync(run, recipient, ct); }
        public Task<WhatsAppContact?> GetContactForUpdateAsync(Guid recipient, CancellationToken ct) { ContactReads++; return Task.FromResult(Contact?.UserId == recipient ? Contact : null); }
        public void Add(WhatsAppOutgoingMessage outgoing) => Outgoing.Add(outgoing);
    }
}
