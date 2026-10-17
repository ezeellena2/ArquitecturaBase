using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppAssistantInterpretationTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    private static WhatsAppAssistantDispatchService Service(WhatsAppAssistantSuggestionFixture host)=>new(host.Context.Policy,
        new(host.Reader,host.Assistant,host.Forms,host.Context.Clock),host.Reader,host.Requests,host.Conversation,host.UnitOfWork,host.Context.Clock,
        NullLogger<WhatsAppAssistantDispatchService>.Instance);
    [Fact]
    public async Task Preparing_manual_work_preserves_the_actual_operator_subject_source_and_retention()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var prepared=await Service(host).PrepareAsync(host.Context.Contact.Id,Ct);
        Assert.True(prepared.IsSuccess);Assert.NotNull(prepared.Value);var operation=prepared.Value.Operation;
        Assert.Equal(host.Request.RequestId,operation.RequestId);Assert.Equal(host.Context.Operator.Id,operation.Context.ActorUserId);
        Assert.Equal(host.Context.Subject.Id,operation.Context.SubjectUserId);Assert.Equal("whatsapp-manual",operation.Context.ChannelId);
        Assert.Equal(host.Reader.Source!.Body,operation.Text);Assert.Equal(host.Reader.Source.OccurredAtUtc,operation.OccurredAtUtc);
        Assert.Equal(host.Reader.Source.ContentExpiresAtUtc,operation.ContentExpiresAtUtc);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task Recovery_prepares_the_same_request_instead_of_allocating_another_model_operation()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var service=Service(host);
        var first=await service.PrepareAsync(host.Context.Contact.Id,Ct);var second=await service.PrepareAsync(host.Context.Contact.Id,Ct);
        Assert.NotNull(first.Value);Assert.NotNull(second.Value);Assert.Equal(first.Value.RequestId,second.Value.RequestId);
        Assert.Single(host.Requests.Rows);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task A_completed_registration_becomes_a_shared_ready_suggestion_without_sending()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var gets=host.Assistant.Gets;var result=await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Ready,host.Requests.Rows[0].State);
        Assert.Equal(host.Assistant.Outcome.Value.Proposal!.Id,host.Requests.Rows[0].ProposalId);Assert.Empty(host.Conversation.Outgoing);
        Assert.Equal(gets,host.Assistant.Gets);Assert.Equal(0,host.Assistant.Submits);
    }
    [Fact]
    public async Task A_no_reply_completion_stays_silent_and_does_not_change_the_inbound_read_marker()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();var proposal=new AssistantObservedProposal(Guid.NewGuid(),Guid.NewGuid(),new("whatsapp.no-reply",1),new WhatsAppAssistantNoReplyPayload("not_form"));
        var outcome=host.Assistant.Outcome.Value with {Proposal=proposal};var inbound=host.Context.Contact.LastInboundAtUtc;
        Assert.True((await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,outcome,Ct)).IsSuccess);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply,host.Requests.Rows[0].State);Assert.Equal(inbound,host.Context.Contact.LastInboundAtUtc);
        Assert.Empty(host.Conversation.Outgoing);
    }
    [Fact]
    public async Task Revoking_the_original_operator_blocks_recovery_instead_of_using_another_account()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();await host.Context.Users.SetActiveAsync(host.Context.Operator.Id,false,Ct);
        var prepared=await Service(host).PrepareAsync(host.Context.Contact.Id,Ct);
        Assert.True(prepared.IsSuccess);Assert.Null(prepared.Value);Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
    }
    [Fact]
    public async Task A_completed_result_after_source_expiry_cannot_reopen_the_reference()
    {
        var host=await WhatsAppAssistantSuggestionFixture.ReadyAsync();host.Context.Clock.Advance(TimeSpan.FromDays(91));
        var result=await Service(host).CompleteAsync(host.Context.Contact.Id,host.Request.RequestId,host.Assistant.Outcome,Ct);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Blocked,host.Requests.Rows[0].State);
        Assert.Empty(host.Conversation.Outgoing);
    }
}
