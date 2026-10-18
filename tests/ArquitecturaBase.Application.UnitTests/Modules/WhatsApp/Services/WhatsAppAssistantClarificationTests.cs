using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;
public sealed class WhatsAppAssistantClarificationTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static WhatsAppAssistantDispatchService Service(WhatsAppAssistantSuggestionFixture host)=>new(host.Context.Policy,
        new(host.Reader,host.Assistant,host.Forms,host.Context.Clock,host.Presentation),host.Reader,host.Requests,host.Conversation,host.UnitOfWork,
        WhatsAppAssistantAcceptanceFixture.CreateAutomatic(host),NullLogger<WhatsAppAssistantDispatchService>.Instance);
    private static async Task<(WhatsAppAssistantSuggestionFixture Host,Guid RequestId)> HostAsync()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Requests.Rows.Clear();host.Reader.Reference=null;
        host.Context.Contact.ChangeMode(WhatsAppConversationMode.Assistant);
        var work=await Service(host).PrepareAsync(host.Context.Contact.Id,Ct);Assert.NotNull(work.Value);
        var proposal=host.Assistant.Outcome.Value.Proposal! with {Capability=new("forms.clarify-registration",1)};
        host.Assistant.Outcome=host.Assistant.Outcome.Value with {RequestId=work.Value.RequestId,Proposal=proposal,
            Provenance=host.Assistant.Outcome.Value.Provenance! with {ChannelId="whatsapp-assistant"}};
        var form=host.Forms.Prefill.Value.Form;
        host.Forms.Clarification=new StructuredFormClarificationResponse("¿Qué formulario querés completar?",
            [new(form.DefinitionId,form.VersionId,"Visita",4),new(Guid.NewGuid(),Guid.NewGuid(),"Compra",5)]);
        return(host,work.Value.RequestId);
    }
    private static void Choose(WhatsAppAssistantSuggestionFixture host,Guid requestId,StructuredFormClarificationChoice choice)
    {
        var message=WhatsAppMessage.Inbound(host.Context.Contact.Id,"wamid.fictitious-choice-"+Guid.NewGuid().ToString("N"),WhatsAppMessageKind.ButtonReply,
            choice.Label,WhatsAppAssistantReplyCodec.Encode(requestId,choice.DefinitionId,choice.VersionId),host.Context.Clock.GetUtcNow().UtcDateTime);
        message.Classify(WhatsAppMessagePurpose.Business);message.MarkProcessed(host.Context.Clock.GetUtcNow().UtcDateTime);message.SetInboundSequence(2);
        host.Messages.Messages.Add(message);
        host.Reader.Choice=new(message.Id,message.WaMessageId,message.ReplyId!,message.OccurredAtUtc);
        host.Reader.PinnedSource=host.Reader.Source;host.Reader.Source=null;
        host.Forms.Clarification=host.Forms.Clarification.Value with {SelectedForm=host.Forms.Prefill.Value with {Fields=[],Reason="Eligió este formulario entre las opciones ofrecidas."}};
    }
    [Fact]
    public async Task Ambiguity_enqueues_one_static_question_with_real_sealed_choices_and_definition_revisions()
    {
        var(host,id)=await HostAsync();Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        var question=Assert.Single(host.Conversation.Outgoing);Assert.Equal(WhatsAppOutgoingMessageKind.QuickReplies,question.Kind);
        Assert.Equal(host.Forms.Clarification.Value.Question,question.Body);Assert.Equal(id,question.RequestId);
        Assert.Equal(2,question.QuickReplies.Count);Assert.Equal(WhatsAppAssistantRequestState.Ready,host.Requests.Rows[0].State);
        Assert.Equal(question.Id,host.Requests.Rows[0].QuestionOutgoingId);Assert.Null(host.Requests.Rows[0].DefinitionId);
        foreach(var reply in question.QuickReplies)
        {
            Assert.True(WhatsAppAssistantReplyCodec.TryParse(reply.Id,out var requestId,out var definitionId,out var versionId));Assert.Equal(id,requestId);
            var expected=Assert.Single(host.Forms.Clarification.Value.Choices,choice=>choice.DefinitionId==definitionId && choice.VersionId==versionId);
            Assert.Equal(expected.Label,reply.Title);Assert.Equal(expected.DefinitionRevision,reply.DefinitionRevision);
        }
        Assert.Empty(host.Sessions.Rows);
    }
    [Fact]
    public async Task Choosing_a_question_recovers_the_existing_result_without_accepting_another_model_operation()
    {
        var(host,id)=await HostAsync();await Service(host).CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Choose(host,id,host.Forms.Clarification.Value.Choices[0]);
        var prepared=await Service(host).PrepareAsync(host.Context.Contact.Id,Ct);Assert.NotNull(prepared.Value);
        Assert.Equal(id,prepared.Value.RequestId);Assert.False(prepared.Value.ShouldSubmit);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task The_second_daily_question_and_its_chosen_flow_share_one_offer_and_distinct_durable_outgoing_ids()
    {
        var(host,id)=await HostAsync();host.Reader.OfferCount=1;var service=Service(host);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        var question=Assert.Single(host.Conversation.Outgoing);host.Reader.OfferCount=2;host.Reader.ExcludedOfferCount=1;
        Choose(host,id,host.Forms.Clarification.Value.Choices[0]);
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        var flow=Assert.Single(host.Conversation.Outgoing,item=>item.Kind==WhatsAppOutgoingMessageKind.Flow);
        Assert.NotEqual(question.RequestId,flow.RequestId);Assert.Single(host.Sessions.Rows);
        Assert.Equal(WhatsAppAssistantRequestState.Sent,host.Requests.Rows[0].State);Assert.Equal(flow.Id,host.Requests.Rows[0].OutgoingId);
        Assert.Equal(id,host.Reader.LastCount!.Value.ExcludingRequestId);Assert.True(host.Forms.LastClarification!.ForSending);
        Assert.Equal("forms.clarify-registration",host.Protector.Content!.Origin.Proposal!.CapabilityId);
        Assert.True((await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(2,host.Conversation.Outgoing.Count);Assert.Single(host.Sessions.Rows);
    }
    [Fact]
    public async Task An_exhausted_quota_or_declined_option_never_enqueues_a_question()
    {
        var(host,id)=await HostAsync();host.Reader.OfferCount=2;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task A_wrong_choice_version_is_consumed_without_poisoning_a_later_valid_choice()
    {
        var(host,id)=await HostAsync();var service=Service(host);await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        var real=host.Forms.Clarification.Value.Choices[0];Choose(host,id,real with {VersionId=Guid.NewGuid()});
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(WhatsAppAssistantRequestState.Ready,host.Requests.Rows[0].State);Assert.Empty(host.Sessions.Rows);
        Assert.NotNull(host.Messages.Messages[0].AssistantProcessedAtUtc);
        Choose(host,id,real);await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Single(host.Sessions.Rows);Assert.Equal(2,host.Conversation.Outgoing.Count);
    }
    [Fact]
    public async Task A_late_choice_after_taking_manual_attention_never_creates_a_flow()
    {
        var(host,id)=await HostAsync();var service=Service(host);await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Choose(host,id,host.Forms.Clarification.Value.Choices[0]);host.Context.Contact.ChangeMode(WhatsAppConversationMode.Manual);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Empty(host.Sessions.Rows);Assert.Single(host.Conversation.Outgoing);Assert.NotNull(host.Messages.Messages[0].AssistantProcessedAtUtc);
    }
    [Theory]
    [InlineData("Registrar mantenimiento","Registrar una visita")]
    [InlineData("Registrar mantenimiento preventivo","Registrar mantenimiento correctivo")]
    public async Task Long_form_names_remain_in_the_question_and_have_distinct_provider_sized_titles(string first,string second)
    {
        var(host,id)=await HostAsync();var choices=host.Forms.Clarification.Value.Choices;
        host.Forms.Clarification=host.Forms.Clarification.Value with {Choices=[choices[0] with {Label=first},choices[1] with {Label=second}]};
        await Service(host).CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        var question=Assert.Single(host.Conversation.Outgoing);Assert.Contains(first,question.Body!,StringComparison.Ordinal);Assert.Contains(second,question.Body!,StringComparison.Ordinal);
        Assert.All(question.QuickReplies,reply=>Assert.InRange(reply.Title.Length,1,20));Assert.Equal(2,question.QuickReplies.Select(reply=>reply.Title).Distinct(StringComparer.Ordinal).Count());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_offered_choice_becomes_blocked_when_its_owner_withdraws_the_flow(bool onlyAtSending)
    {
        var(host,id)=await HostAsync();var service=Service(host);await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Choose(host,id,host.Forms.Clarification.Value.Choices[0]);
        if(onlyAtSending)host.Forms.SendingClarification=FlowErrors.StateConflict;
        else host.Forms.Clarification=FlowErrors.StateConflict;
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);Assert.Empty(host.Sessions.Rows);Assert.Single(host.Conversation.Outgoing);
        Assert.NotNull(host.Messages.Messages[0].AssistantProcessedAtUtc);
        await service.CompleteAsync(host.Context.Contact.Id,id,host.Assistant.Outcome,Ct);Assert.Single(host.Conversation.Outgoing);
    }
}
