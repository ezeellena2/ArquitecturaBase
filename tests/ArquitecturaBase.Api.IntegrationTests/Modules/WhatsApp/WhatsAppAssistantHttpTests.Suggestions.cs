using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.WhatsApp;

public sealed partial class WhatsAppAssistantHttpTests
{
    [Fact]
    public async Task Configured_Always_in_manual_attention_exports_the_real_ready_pointer_and_send_without_a_model_proposal()
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Always";
        host.Operations.AvailabilityState="notInstalled";var contact=await host.ContactAsync(Ct);
        var parent=await WhatsAppAssistantCaptureFixture.SendManualParentAsync(host,contact,Ct);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);
        await C4ProcessAsync(host);var reference=await C4ReferenceAsync(host,captured.RecordId);
        Assert.True(reference.IsAlways);Assert.Null(reference.ActorUserId);Assert.Null(reference.ContextJson);Assert.Null(reference.ProposalId);
        Assert.Equal(captured.MessageId,reference.SourceMessageId);
        using var actor=host.Client(host.FirstOperator);var route=Route(contact.ContactId)+"/"+reference.RequestId;
        using var ready=await actor.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.OK,ready.StatusCode);var readyBody=await ready.ReadJsonAsync();AssertState(readyBody,"Ready");
        var proposal=readyBody.GetProperty("proposal");Assert.Equal(JsonValueKind.Null,proposal.GetProperty("proposalId").ValueKind);
        Assert.Equal(host.Forms.AlternativeForm.DefinitionId,proposal.GetProperty("definitionId").GetGuid());
        Assert.Equal("Sucursal Norte",Assert.Single(proposal.GetProperty("fields").EnumerateArray()).GetProperty("displayValue").GetString());
        await WhatsAppAssistantContractCapture.WriteAsync(ready,"suggestion-always-ready",Ct);
        using var thread=await actor.GetAsync($"/api/whatsapp/chats/{contact.ContactId}/messages",Ct);Assert.Equal(HttpStatusCode.OK,thread.StatusCode);
        var threadBody=await thread.ReadJsonAsync();Assert.Equal("notInstalled",threadBody.GetProperty("assistantAvailability").GetProperty("state").GetString());
        Assert.Equal(reference.RequestId,threadBody.GetProperty("assistantSuggestion").GetProperty("requestId").GetGuid());
        Assert.Equal(captured.MessageId,threadBody.GetProperty("assistantSuggestion").GetProperty("sourceMessageId").GetGuid());
        await WhatsAppAssistantContractCapture.WriteAsync(thread,"thread-always-ready",Ct);
        using var sent=await actor.PostAsJsonAsync(route+"/send",new{requestId=Guid.NewGuid(),includedFieldIds=new[]{host.Forms.AlternativeFieldId}},Ct);
        Assert.Equal(HttpStatusCode.Accepted,sent.StatusCode);var receipt=await sent.ReadJsonAsync();
        Assert.Equal(host.FirstOperator,receipt.GetProperty("operatorUserId").GetGuid());
        Assert.Equal("Continuación de la visita confirmada.",receipt.GetProperty("assistant").GetProperty("reason").GetString());
        await WhatsAppAssistantContractCapture.WriteAsync(sent,"always-send-receipt",Ct);
        using var second=host.Client(host.SecondOperator);using var replay=await second.PostAsJsonAsync(route+"/send",new{requestId=Guid.NewGuid(),includedFieldIds=Array.Empty<Guid>()},Ct);
        Assert.Equal(HttpStatusCode.Accepted,replay.StatusCode);Assert.Equal(receipt.GetProperty("id").GetGuid(),(await replay.ReadJsonAsync()).GetProperty("id").GetGuid());
        Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));Assert.DoesNotContain(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);Assert.DoesNotContain(host.Operations.ReadContexts,context=>context.SubjectUserId==contact.RecipientId);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        var child=Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages.Last());
        await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,child,host.Forms.AlternativeFieldId,Ct);
        var origin=host.Host.Services.GetRequiredService<CapturingStructuredRecordPort>().Captures[^1].Origin;Assert.NotNull(origin);
        Assert.Equal(ArquitecturaBase.Application.Models.StructuredData.StructuredRecordSenderKind.Manual,origin.SenderKind);
        Assert.Equal(host.FirstOperator,origin.OperatorUserId);Assert.Null(origin.Proposal);Assert.Equal(captured.RecordId,origin.Suggestion!.OriginRecordId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_Always_requires_current_historical_record_permission_before_reading_or_sending(bool revokeAfterPreview)
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Always";
        var contact=await host.ContactAsync(Ct);var parent=await WhatsAppAssistantCaptureFixture.SendManualParentAsync(host,contact,Ct);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);await C4ProcessAsync(host);
        var reference=await C4ReferenceAsync(host,captured.RecordId);var route=Route(contact.ContactId)+"/"+reference.RequestId;
        using var reader=host.Client(revokeAfterPreview?host.FirstOperator:host.RestrictedOperator);
        if(revokeAfterPreview)
        {
            using var before=await reader.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.OK,before.StatusCode);
            await host.InTransactionAsync(async services=>
            {
                var role="assistant-current-without-records-"+Guid.NewGuid().ToString("N");
                await services.GetRequiredService<IRoleRepository>().CreateAsync(role,null,[WhatsAppPermissions.Read,WhatsAppPermissions.Send],Ct);
                await services.GetRequiredService<IUserRepository>().SetRolesAsync(host.FirstOperator,[role],Ct);return true;
            },Ct);
        }
        using var denied=await reader.GetAsync(route,Ct);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using var send=await reader.PostAsJsonAsync(route+"/send",new{requestId=Guid.NewGuid(),includedFieldIds=new[]{host.Forms.AlternativeFieldId}},Ct);
        Assert.Equal(HttpStatusCode.Forbidden,send.StatusCode);Assert.Equal((1,1),await C4CountsAsync(host,contact.ContactId));
    }

    [Fact]
    public async Task Automatic_Always_works_with_AI_unavailable_and_deduplicates_both_the_receipt_and_an_equivalent_new_flow_reply()
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Always";
        var contact=await host.ContactAsync(Ct,automatic:true);var parent=await C4AutomaticParentAsync(host,contact);
        host.Operations.AvailabilityState="notInstalled";
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);await C4ProcessAsync(host);
        var reference=await C4ReferenceAsync(host,captured.RecordId);Assert.Equal(WhatsAppAssistantRequestState.Sent,reference.State);Assert.Null(reference.ProposalId);
        Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));
        await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct,captured.WaMessageId);await C4ProcessAsync(host);
        var equivalent=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);await C4ProcessAsync(host);
        Assert.Equal(captured.RecordId,equivalent.RecordId);Assert.NotEqual(captured.MessageId,equivalent.MessageId);
        Assert.Equal(reference.RequestId,(await C4ReferenceAsync(host,captured.RecordId)).RequestId);
        Assert.Equal(captured.MessageId,(await C4ReferenceAsync(host,captured.RecordId)).SourceMessageId);
        Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));Assert.DoesNotContain(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);Assert.DoesNotContain(host.Operations.ReadContexts,context=>context.SubjectUserId==contact.RecipientId);
    }

    [Fact]
    public async Task Automatic_Always_does_not_follow_a_configured_loop_even_with_one_daily_offer_slot_left()
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Always";host.Forms.SuggestionCreatesLoop=true;
        var contact=await host.ContactAsync(Ct,automatic:true);var parent=await C4AutomaticParentAsync(host,contact);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);await C4ProcessAsync(host);
        Assert.Equal((1,1),await C4CountsAsync(host,contact.ContactId));Assert.DoesNotContain(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,(await C4ReferenceAsync(host,captured.RecordId)).State);
    }

    [Fact]
    public async Task Automatic_Always_cannot_be_a_third_daily_offer()
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Always";
        var contact=await host.ContactAsync(Ct,automatic:true);var parent=await C4AutomaticParentAsync(host,contact);
        contact=contact with {SourceMessageId=await host.AddNewerSourceAsync(contact,Ct)};await C4AutomaticParentAsync(host,contact);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);await C4ProcessAsync(host);
        Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));Assert.DoesNotContain(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,(await C4ReferenceAsync(host,captured.RecordId)).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_configured_Assistant_suggestion_uses_the_same_operation_and_requires_manual_confirmation_only_in_manual_attention(bool automatic)
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Assistant";
        var contact=await host.ContactAsync(Ct,automatic);var parent=automatic?await C4AutomaticParentAsync(host,contact)
            :await WhatsAppAssistantCaptureFixture.SendManualParentAsync(host,contact,Ct);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);
        if(automatic)
        {
            host.Operations.AllowSubmit=true;await C4ProcessAsync(host);
            var accepted=Assert.Single(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);
            Assert.Equal(contact.RecipientId,accepted.Context.ActorUserId);Assert.Equal(contact.RecipientId,accepted.Context.SubjectUserId);
            Assert.Contains(accepted.Context.AllowedCapabilities,item=>item.Id=="forms.propose-suggestion");
            Assert.DoesNotContain(accepted.Context.AllowedCapabilities,item=>item.Id=="forms.propose-registration");
            host.Outcome(contact,new(accepted.RequestId,accepted),"suggestion");await C4ProcessAsync(host);await C4ProcessAsync(host);
            Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));Assert.Single(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);
        }
        else
        {
            await C4ProcessAsync(host);Assert.DoesNotContain(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);Assert.Equal((1,1),await C4CountsAsync(host,contact.ContactId));
            using var actor=host.Client(host.FirstOperator);var id=Guid.NewGuid();
            using var requested=await RequestAsync(actor,contact with {SourceMessageId=captured.MessageId},id);Assert.Equal(HttpStatusCode.Accepted,requested.StatusCode);
            await host.CompleteAsync(contact,id,"suggestion",Ct);
            using var ready=await actor.GetAsync(Route(contact.ContactId)+"/"+id,Ct);Assert.Equal(HttpStatusCode.OK,ready.StatusCode);AssertState(await ready.ReadJsonAsync(),"Ready");
            Assert.Equal((1,1),await C4CountsAsync(host,contact.ContactId));
            using var send=await actor.PostAsJsonAsync(Route(contact.ContactId)+"/"+id+"/send",new{requestId=Guid.NewGuid(),includedFieldIds=new[]{host.Forms.AlternativeFieldId}},Ct);
            Assert.Equal(HttpStatusCode.Accepted,send.StatusCode);Assert.Equal((2,2),await C4CountsAsync(host,contact.ContactId));
            Assert.Equal(host.FirstOperator,host.Forms.SuggestionPreparations.Last(request=>request.ForSending && request.RecipientUserId==contact.RecipientId).ActorUserId);
        }
        Assert.Equal(captured.RecordId,(await C4ReferenceAsync(host,captured.RecordId)).SourceRecordId);
    }

    [Fact]
    public async Task Taking_manual_attention_before_the_configured_Assistant_result_keeps_the_captured_visit_without_an_automatic_followup()
    {
        await using var host=await WhatsAppAssistantCaptureFixture.CreateAsync(factory,Ct);host.Forms.SuggestionMode="Assistant";
        var contact=await host.ContactAsync(Ct,automatic:true);var parent=await C4AutomaticParentAsync(host,contact);
        var captured=await WhatsAppAssistantCaptureFixture.ReplyAsync(host,contact,parent,host.Forms.FieldId,Ct);
        host.Operations.AllowSubmit=true;await C4ProcessAsync(host);var submitted=Assert.Single(host.Operations.SubmitRequests,request=>request.Context.SubjectUserId==contact.RecipientId);
        var outcome=host.Outcome(contact,new(submitted.RequestId,submitted),"suggestion");
        await host.InTransactionAsync(async services=>{var row=await services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppContact>().SingleAsync(item=>item.Id==contact.ContactId,Ct);
            row.ChangeMode(WhatsAppConversationMode.Manual);return true;},Ct);
        await host.CompleteOutcomeAsync(contact.ContactId,outcome,Ct);Assert.Equal((1,1),await C4CountsAsync(host,contact.ContactId));
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,(await C4ReferenceAsync(host,captured.RecordId)).State);
        Assert.True(await host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>().Set<WhatsAppFlowSession>().AsNoTracking()
            .AnyAsync(item=>item.Id==captured.SessionId && item.CompletedRecordId==captured.RecordId && item.CaptureState==WhatsAppFlowCaptureState.Completed,Ct)));
    }

    private static Task C4ProcessAsync(WhatsAppAssistantScenario host)=>host.Host.Services.GetRequiredService<WhatsAppInboundProcessor>().ProcessPendingAsync(Ct);
    private static Task<WhatsAppAssistantRequest> C4ReferenceAsync(WhatsAppAssistantScenario host,Guid sourceRecordId)=>host.ScopeAsync(services=>services.GetRequiredService<ApplicationDbContext>()
        .Set<WhatsAppAssistantRequest>().AsNoTracking().SingleAsync(item=>item.SourceRecordId==sourceRecordId,Ct));
    private static Task<(int Outgoing,int Sessions)> C4CountsAsync(WhatsAppAssistantScenario host,Guid contactId)=>host.ScopeAsync(async services=>
    {var db=services.GetRequiredService<ApplicationDbContext>();return(await db.Set<WhatsAppOutgoingMessage>().CountAsync(item=>item.ContactId==contactId,Ct),
        await db.Set<WhatsAppFlowSession>().CountAsync(item=>item.ContactId==contactId,Ct));});
    private static async Task<WhatsAppFlowMessageResponse> C4AutomaticParentAsync(WhatsAppAssistantScenario host,WhatsAppAssistantContact contact)
    {
        var work=await host.ScopeAsync(services=>services.GetRequiredService<IWhatsAppAssistantDispatchService>().PrepareAsync(contact.ContactId,Ct));Assert.True(work.IsSuccess);Assert.NotNull(work.Value);
        await host.CompleteOutcomeAsync(contact.ContactId,host.Outcome(contact,work.Value,"ready"),Ct);
        await host.Host.Services.GetRequiredService<WhatsAppOutgoingBackgroundService>().ProcessNextAsync(Ct);
        return Assert.IsType<WhatsAppFlowMessageResponse>(host.Cloud.Messages.Last());
    }
}
