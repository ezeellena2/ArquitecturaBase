using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppConfiguredSuggestionTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static async Task<(WhatsAppAssistantSuggestionFixture Host,WhatsAppAssistantRequest Row)> ArrangeAsync()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Assistant.Availability="paused";host.Assistant.ResetCalls();host.Requests.Rows.Clear();
        var record=Guid.NewGuid();var set=Guid.NewGuid();var suggestion=Guid.NewGuid();var now=host.Context.Clock.GetUtcNow().UtcDateTime;
        var row=WhatsAppAssistantRequest.CreateConfiguredManual(Guid.NewGuid(),host.Context.Contact.Id,host.SourceId,host.Context.Subject.Id,
            record,set,suggestion,"es",host.Context.Contact.ModeRevision,host.Context.Contact.LinkRevision,now,now.AddDays(90));
        host.Requests.Rows.Add(row);host.Reader.Reference=WhatsAppAssistantSuggestionPolicy.Reference(row);
        host.Reader.Source=host.Reader.Source! with {CapturedRecordId=record};
        var origin=new StructuredRecordSuggestionOrigin(record,suggestion,set,[]);
        host.Forms.Suggestion=new StructuredFormSuggestionPreparationResponse(host.Forms.Prefill.Value,host.Forms.Prefill.Value.Fields,origin,false);
        return(host,row);
    }
    [Fact]
    public async Task Reading_always_uses_the_current_operator_and_no_AI_even_when_the_connection_is_paused()
    {
        var(host,row)=await ArrangeAsync();var result=await host.Service.GetAsync(host.Context.Contact.Id,row.RequestId,Ct);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Ready,result.Value.State);Assert.NotNull(result.Value.Proposal);Assert.Null(result.Value.Proposal.ProposalId);
        Assert.Equal(host.Context.Operator.Id,host.Forms.LastSuggestion!.ActorUserId);Assert.Null(host.Forms.LastSuggestion.Proposal);
        Assert.Equal(0,host.Assistant.Gets);Assert.Equal(0,host.Assistant.Submits);Assert.Equal(0,host.Assistant.AvailabilityReads);
    }
    [Fact]
    public async Task An_existing_always_request_replays_while_AI_is_unavailable_without_inventing_an_actor_in_the_reference()
    {
        var(host,row)=await ArrangeAsync();var result=await host.Service.RequestAsync(host.Request with {RequestId=Guid.NewGuid()},Ct);
        Assert.True(result.IsSuccess);Assert.Equal(row.RequestId,result.Value.RequestId);Assert.Equal(WhatsAppAssistantRequestState.Ready,result.Value.State);
        Assert.Null(row.ActorUserId);Assert.Null(row.ContextJson);Assert.Equal(0,host.Assistant.Gets);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task Confirming_always_reuses_the_real_session_and_queue_with_a_factual_origin_and_no_model_proposal()
    {
        var(host,row)=await ArrangeAsync();var field=host.Forms.Prefill.Value.Fields[0];var sendId=Guid.NewGuid();
        var request=new ArquitecturaBase.Application.Modules.WhatsApp.Models.SendWhatsAppAssistantSuggestionRequest(host.Context.Contact.Id,row.RequestId,sendId,[field.FieldId]);
        var sent=await host.Service.SendAsync(request,Ct);Assert.True(sent.IsSuccess);var outgoing=Assert.Single(host.Conversation.Outgoing);
        Assert.Equal(host.Context.Operator.Id,outgoing.OperatorUserId);Assert.Equal(WhatsAppMessageOrigin.Conversation,outgoing.Origin);
        var origin=host.Protector.Content!.Origin;Assert.Null(origin.Proposal);Assert.Equal(host.Forms.Suggestion.Value.OriginSuggestion,origin.Suggestion);
        Assert.Equal(2,origin.ProposedValues.Count);Assert.Equal(new[]{field.FieldId},origin.IncludedFieldIds);Assert.True(host.Forms.LastSuggestion!.ForSending);
        Assert.Equal(host.Context.Operator.Id,host.Forms.LastSuggestion.ActorUserId);Assert.Equal(WhatsAppAssistantRequestState.Sent,row.State);
        Assert.True((await host.Service.SendAsync(request,Ct)).IsSuccess);Assert.Single(host.Sessions.Rows);Assert.Single(host.Conversation.Outgoing);
        Assert.Equal(0,host.Assistant.Gets);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task The_owner_can_withdraw_all_historical_values_instead_of_returning_an_empty_ready_offer()
    {
        var(host,row)=await ArrangeAsync();host.Forms.Suggestion=WhatsAppAssistantErrors.Forbidden;
        var result=await host.Service.GetAsync(host.Context.Contact.Id,row.RequestId,Ct);
        Assert.True(result.IsFailure);Assert.Equal(WhatsAppAssistantErrors.Forbidden,result.Error);Assert.Equal(0,host.Assistant.Gets);
    }
    [Fact]
    public async Task Dismissing_always_is_durable_without_calling_the_model_or_sending()
    {
        var(host,row)=await ArrangeAsync();var result=await host.Service.DiscardAsync(host.Context.Contact.Id,row.RequestId,Ct);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Discarded,row.State);Assert.Equal(host.Context.Operator.Id,row.DecidedByUserId);
        Assert.Null(row.ProposalId);Assert.Empty(host.Conversation.Outgoing);Assert.Equal(0,host.Assistant.Gets);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task A_configured_replay_never_checks_AI_availability()
    {
        var(host,_)=await ArrangeAsync();await host.Service.RequestAsync(host.Request,Ct);
        Assert.Equal(0,host.Assistant.AvailabilityReads);
    }
}
