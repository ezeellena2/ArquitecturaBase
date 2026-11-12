using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantTerminalDispatchTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static async Task<(WhatsAppAssistantSuggestionFixture Host,WhatsAppAssistantDispatchService Service,Sinks Sinks,Guid Id)> ArrangeAsync(bool automatic,string capability)
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();
        var sinks=new Sinks(host);
        var service=new WhatsAppAssistantDispatchService(host.Context.Policy,new(host.Reader,host.Assistant,host.Forms,host.Context.Clock,host.Presentation),
            host.Reader,host.Requests,host.Conversation,host.UnitOfWork,new(WhatsAppAssistantAcceptanceFixture.Create(host),host.Reader,
            host.Context.Clock,host.Conversation,host.Messages,new(sinks,sinks,sinks,host.Reader),new(host.Reader,host.Context.Clock),
            WhatsAppAssistantAcceptanceFixture.CreateReply(host)),NullLogger<WhatsAppAssistantDispatchService>.Instance);
        var id=host.Request.RequestId;
        if(automatic)
        {
            host.Requests.Rows.Clear();host.Reader.Reference=null;host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
            var work=await service.PrepareAsync(host.Context.Contact.Id,Ct);id=work.Value!.RequestId;
        }
        host.Assistant.Outcome=host.Assistant.Outcome.Value with {RequestId=id,
            Proposal=host.Assistant.Outcome.Value.Proposal! with {Capability=new(capability,1)},
            Provenance=host.Assistant.Outcome.Value.Provenance! with {ChannelId=automatic?"whatsapp-assistant":"whatsapp-manual"}};
        return(host,service,sinks,id);
    }
    [Fact]
    public async Task Automatic_terminal_commits_the_owner_outside_the_channel_transaction_and_one_fixed_ack_without_offer_quota()
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,"forms.missing-request.propose");var before=host.Reader.LastCount;host.Reader.OfferCount=2;
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(1,sinks.Calls);Assert.Equal(WhatsAppAssistantRequestState.Sent,host.Requests.Rows[0].State);
        var outgoing=Assert.Single(host.Conversation.Outgoing);Assert.Equal(WhatsAppOutgoingMessageKind.Text,outgoing.Kind);
        Assert.Equal(WhatsAppMessageOrigin.Assistant,outgoing.Origin);Assert.Null(outgoing.OperatorUserId);Assert.Equal(id,outgoing.RequestId);
        Assert.DoesNotContain(host.Reader.Source!.Body,outgoing.Body!,StringComparison.Ordinal);Assert.Equal(before,host.Reader.LastCount);Assert.Empty(host.Sessions.Rows);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(1,sinks.Calls);Assert.Single(host.Conversation.Outgoing);
    }
    [Theory]
    [InlineData("forms.catalog-filters.propose")]
    [InlineData("forms.authoring.propose-form")]
    [InlineData("forms.authoring.propose-catalog")]
    public async Task An_automatic_authoring_proposal_sends_its_summary_with_two_buttons_creates_nothing_and_is_not_an_offer(string capability)
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,capability);var before=host.Reader.LastCount;host.Reader.OfferCount=2;
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal((0,1),(sinks.Calls,sinks.Summaries));Assert.Equal(WhatsAppAssistantRequestState.Sent,host.Requests.Rows[0].State);
        var outgoing=Assert.Single(host.Conversation.Outgoing);Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies,outgoing.Kind);
        Assert.Equal(host.Requests.Rows[0].OutgoingId,outgoing.Id);Assert.Equal(host.Assistant.Outcome.Value.Proposal!.Id,host.Requests.Rows[0].ProposalId);
        Assert.Equal([WhatsAppAssistantAuthoringReplyCodec.Create,WhatsAppAssistantAuthoringReplyCodec.Change],outgoing.QuickReplies.Select(reply=>
            WhatsAppAssistantAuthoringReplyCodec.TryParse(reply.Id,out var request,out var proposal,out var action)&&request==id&&proposal==host.Requests.Rows[0].ProposalId?action:null));
        Assert.Equal(before,host.Reader.LastCount);Assert.Empty(host.Sessions.Rows);
    }
    // «Pregunta y espera»: the question is a fixed text, not an offer, and its reference waits for the answer that continues it.
    [Fact]
    public async Task An_unclear_request_sends_its_fixed_question_and_waits_for_the_answer_without_an_offer()
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,"forms.authoring.decline");var before=host.Reader.LastCount;host.Reader.OfferCount=2;
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        var row=host.Requests.Rows[0];
        Assert.Equal((0,1,WhatsAppAssistantRequestState.Sent,(Guid?)id),(sinks.Calls,sinks.Summaries,row.State,row.ChangeRequestId));
        var outgoing=Assert.Single(host.Conversation.Outgoing);
        Assert.Equal((WhatsAppOutgoingMessageKind.Text,row.OutgoingId),(outgoing.Kind,(Guid?)outgoing.Id));
        Assert.StartsWith("No me queda claro qué querés que arme.",outgoing.Body,StringComparison.Ordinal);
        Assert.Equal(before,host.Reader.LastCount);Assert.Empty(host.Sessions.Rows);
    }
    [Theory]
    [InlineData("forms.catalog-filters.propose")]
    [InlineData("forms.missing-request.propose")]
    public async Task Manual_analysis_records_the_owner_result_without_sending_to_the_phone(string capability)
    {
        var(host,service,sinks,id)=await ArrangeAsync(false,capability);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(capability=="forms.missing-request.propose"?1:0,sinks.Calls);Assert.Equal(WhatsAppAssistantRequestState.NoReply,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);Assert.Empty(host.Sessions.Rows);
    }
    [Fact]
    public async Task A_stale_channel_context_never_calls_an_owner_sink()
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,"forms.missing-request.propose");host.Reader.CurrentContext=false;
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(0,sinks.Calls);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task Taking_manual_after_the_owner_commits_preserves_its_result_without_an_automatic_ack()
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,"forms.missing-request.propose");
        sinks.AfterCommit=()=>host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(1,sinks.Calls);Assert.Empty(host.Conversation.Outgoing);Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
    }
    [Fact]
    public async Task An_owner_failure_does_not_confirm_or_enqueue_a_terminal()
    {
        var(host,service,sinks,id)=await ArrangeAsync(true,"forms.missing-request.propose");sinks.Failure=true;
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(1,sinks.Calls);Assert.Empty(host.Conversation.Outgoing);Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
    }
    private sealed class Sinks(WhatsAppAssistantSuggestionFixture host):IStructuredAuthoringProposalChannel,IStructuredMissingFormRequestSink,IPublicOrigin
    {
        public int Calls {get;private set;}
        public int Summaries {get;private set;}
        public bool Failure {get;set;}
        public Action? AfterCommit {get;set;}
        public Uri? Value=>new("https://business.example.test/");
        private void Called(){Assert.False(host.UnitOfWork.InTransaction);Calls++;AfterCommit?.Invoke();}
        // The owner's summary is pure; creating only happens from the button, never from a terminal.
        public StructuredAuthoringSummary? Summarize(Guid requestId,AssistantObservedProposal proposal,string culture)
        {Summaries++;return proposal.Capability.Id switch
        {
            "forms.authoring.propose-form"=>new(StructuredAuthoringKind.Form,"Parte de lluvias · LLU",["1. Milímetros · número"],0),
            "forms.authoring.decline"=>new(StructuredAuthoringKind.Declined,"",[],0){Reason="unclear"},
            _=>new(StructuredAuthoringKind.Catalog,"Contratistas",["el código es el CUIT y cada opción la razón social"],0),
        };}
        public Task<Result<StructuredAuthoringCreatedResponse>> CreateAsync(StructuredAuthoringProposalRequest request,CancellationToken ct)=>throw new NotSupportedException();
        public Task<Result<StructuredMissingFormRequestCaptureResponse>> CaptureAsync(StructuredMissingFormRequestCaptureRequest request,CancellationToken ct)
        {Called();Assert.Equal(host.SourceId,request.SourceReceiptId);return Task.FromResult(Failure?Result.Failure<StructuredMissingFormRequestCaptureResponse>(WhatsAppAssistantErrors.Unavailable):Result.Success(new StructuredMissingFormRequestCaptureResponse(Guid.NewGuid(),Guid.NewGuid(),false)));}
    }
}
