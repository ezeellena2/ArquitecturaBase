using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppConfiguredDispatchTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;
    [Fact]
    public async Task A_manual_capture_prepares_the_first_configured_offer_without_an_actor_or_AI_operation()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);var work=await fixture.PrepareAsync();
        Assert.Null(work.Operation);Assert.False(work.ShouldSubmit);var row=Assert.Single(fixture.Host.Requests.Rows);
        Assert.Equal(fixture.Source.Id,row.SourceMessageId);Assert.Equal(fixture.RecordId,row.SourceRecordId);Assert.Equal(fixture.SuggestionId,row.SuggestionId);
        Assert.Null(row.ActorUserId);Assert.Null(row.ContextJson);Assert.NotNull(fixture.Source.AssistantProcessedAtUtc);
    }
    [Fact]
    public async Task Completing_manual_always_produces_one_durable_ready_pointer_without_sending()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);var work=await fixture.PrepareAsync();await fixture.CompleteAsync(work.RequestId);
        var row=Assert.Single(fixture.Host.Requests.Rows);Assert.Equal(WhatsAppAssistantRequestState.Ready,row.State);Assert.Null(row.ProposalId);
        await fixture.CompleteAsync(work.RequestId);Assert.Single(fixture.Host.Requests.Rows);Assert.Empty(fixture.Host.Conversation.Outgoing);
        var preview=await fixture.Host.Service.GetAsync(fixture.Host.Context.Contact.Id,row.RequestId,Ct);Assert.True(preview.IsSuccess);Assert.Null(preview.Value.Proposal!.ProposalId);
        Assert.Equal(0,fixture.Host.Assistant.Gets);Assert.Equal(0,fixture.Host.Assistant.Submits);Assert.Equal(0,fixture.Host.Assistant.AvailabilityReads);
    }
    [Fact]
    public async Task Automatic_always_uses_the_same_session_and_queue_while_AI_is_paused()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);var work=await fixture.PrepareAsync();Assert.Null(work.Operation);
        await fixture.CompleteAsync(work.RequestId);var row=Assert.Single(fixture.Host.Requests.Rows);Assert.Equal(WhatsAppAssistantRequestState.Sent,row.State);
        var outgoing=Assert.Single(fixture.Host.Conversation.Outgoing);Assert.Equal(WhatsAppMessageOrigin.Assistant,outgoing.Origin);Assert.Null(outgoing.OperatorUserId);Assert.Single(fixture.Host.Sessions.Rows);
        var origin=fixture.Host.Protector.Content!.Origin;Assert.Null(origin.Proposal);Assert.Equal(fixture.RecordId,origin.Suggestion!.OriginRecordId);
        await fixture.CompleteAsync(work.RequestId);Assert.Single(fixture.Host.Conversation.Outgoing);Assert.Single(fixture.Host.Sessions.Rows);
        Assert.Equal(0,fixture.Host.Assistant.Gets);Assert.Equal(0,fixture.Host.Assistant.Submits);Assert.Equal(0,fixture.Host.Assistant.AvailabilityReads);
    }
    [Fact]
    public async Task A_second_receipt_for_the_same_record_cannot_allocate_another_offer_or_extend_its_lifetime()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);var work=await fixture.PrepareAsync();await fixture.CompleteAsync(work.RequestId);
        var row=Assert.Single(fixture.Host.Requests.Rows);var expiry=row.ContentExpiresAtUtc;
        fixture.Host.Context.Clock.Advance(TimeSpan.FromMinutes(1));WhatsAppConfiguredDispatchFixture.Capture(fixture.Host,fixture.RecordId,2);
        var replay=await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id,Ct);Assert.True(replay.IsSuccess);Assert.Null(replay.Value);
        Assert.Single(fixture.Host.Requests.Rows);Assert.Single(fixture.Host.Conversation.Outgoing);Assert.Equal(expiry,row.ContentExpiresAtUtc);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Automatic_always_preserves_the_offer_limit_and_the_no_ancestor_loop_rule(bool loop)
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);fixture.Host.Reader.OfferCount=loop?0:2;
        fixture.Host.Forms.Suggestion=fixture.Host.Forms.Suggestion.Value with {CreatesAutomaticLoop=loop};
        var work=await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id,Ct);
        Assert.True(work.IsSuccess);
        if(loop) await fixture.CompleteAsync(Assert.IsType<ArquitecturaBase.Application.Modules.WhatsApp.Models.WhatsAppAssistantWorkResponse>(work.Value).RequestId);
        else Assert.Null(work.Value);

        Assert.Equal(WhatsAppAssistantRequestState.Blocked,fixture.Host.Requests.Rows[0].State);Assert.Empty(fixture.Host.Conversation.Outgoing);Assert.Empty(fixture.Host.Sessions.Rows);
    }
    [Fact]
    public async Task A_human_can_confirm_a_configured_loop_without_an_automatic_offer()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);fixture.Host.Forms.Suggestion=fixture.Host.Forms.Suggestion.Value with {CreatesAutomaticLoop=true};
        var work=await fixture.PrepareAsync();await fixture.CompleteAsync(work.RequestId);
        var result=await fixture.Host.Service.SendAsync(new(fixture.Host.Context.Contact.Id,work.RequestId,Guid.NewGuid(),[]),Ct);
        Assert.True(result.IsSuccess);Assert.Single(fixture.Host.Sessions.Rows);Assert.Equal(fixture.Host.Context.Operator.Id,fixture.Host.Conversation.Outgoing[0].OperatorUserId);
    }
    [Fact]
    public async Task A_newer_business_message_cancels_a_late_configured_completion()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);var work=await fixture.PrepareAsync();
        fixture.Host.Reader.Source=fixture.Host.Reader.Source! with {Id=Guid.NewGuid(),CapturedRecordId=null,Body="A different business message"};
        await fixture.CompleteAsync(work.RequestId);Assert.Equal(WhatsAppAssistantRequestState.Blocked,fixture.Host.Requests.Rows[0].State);Assert.Empty(fixture.Host.Conversation.Outgoing);
    }
    [Fact]
    public async Task Assistant_followups_use_a_real_context_restricted_to_configured_suggestions()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);var candidate=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {Mode="Assistant",When="When another visit is needed"};
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[candidate]};
        var work=await fixture.PrepareAsync();Assert.NotNull(work.Operation);Assert.Equal(fixture.RecordId,fixture.Host.Requests.Rows[0].SourceRecordId);
        Assert.Equal(fixture.Host.Context.Subject.Id,work.Operation.Context.ActorUserId);Assert.Contains(work.Operation.Context.AllowedCapabilities,item=>item.Id=="forms.propose-suggestion");
        Assert.DoesNotContain(work.Operation.Context.AllowedCapabilities,item=>item.Id is "forms.propose-registration" or "forms.missing-request.propose" or "forms.catalog-filters.propose");
    }

    [Fact]
    public async Task The_first_manual_request_discovers_always_while_AI_is_paused_without_waiting_for_the_worker()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);
        var result=await fixture.Host.Service.RequestAsync(new(fixture.Host.Context.Contact.Id,Guid.NewGuid(),fixture.Source.Id,fixture.Host.Context.Contact.ModeRevision),Ct);
        Assert.True(result.IsSuccess);Assert.Equal(WhatsAppAssistantRequestState.Ready,result.Value.State);Assert.Null(result.Value.Proposal!.ProposalId);
        var row=Assert.Single(fixture.Host.Requests.Rows);Assert.Equal(fixture.RecordId,row.SourceRecordId);Assert.Null(row.ActorUserId);Assert.Null(row.ContextJson);
        Assert.Equal(0,fixture.Host.Assistant.AvailabilityReads);Assert.Equal(0,fixture.Host.Assistant.Gets);Assert.Equal(0,fixture.Host.Assistant.Submits);
    }
    [Fact]
    public async Task A_manual_configured_interpretation_seals_the_real_operator_and_only_configured_capabilities()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);fixture.Host.Assistant.Availability="available";fixture.Host.Assistant.Outcome=WhatsAppChatErrors.NotFound;
        var candidate=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {Mode="Assistant",When="When a followup is needed"};
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[candidate]};
        var result=await fixture.Host.Service.RequestAsync(new(fixture.Host.Context.Contact.Id,Guid.NewGuid(),fixture.Source.Id,fixture.Host.Context.Contact.ModeRevision),Ct);
        Assert.True(result.IsSuccess);var row=Assert.Single(fixture.Host.Requests.Rows);Assert.Equal(fixture.RecordId,row.SourceRecordId);
        var context=ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppAssistantSuggestionPolicy.Context(ArquitecturaBase.Application.Modules.WhatsApp.Services.WhatsAppAssistantSuggestionPolicy.Reference(row));
        Assert.Equal(fixture.Host.Context.Operator.Id,context.ActorUserId);Assert.Equal(fixture.Host.Context.Subject.Id,context.SubjectUserId);
        Assert.DoesNotContain(context.AllowedCapabilities,item=>item.Id is "forms.propose-registration" or "forms.missing-request.propose");
    }
    [Fact]
    public async Task A_direct_request_for_a_duplicate_capture_returns_the_same_configured_reference()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);var work=await fixture.PrepareAsync();await fixture.CompleteAsync(work.RequestId);
        var duplicate=WhatsAppConfiguredDispatchFixture.Capture(fixture.Host,fixture.RecordId,2);
        var result=await fixture.Host.Service.RequestAsync(new(fixture.Host.Context.Contact.Id,Guid.NewGuid(),duplicate.Id,fixture.Host.Context.Contact.ModeRevision),Ct);
        Assert.True(result.IsSuccess);Assert.Equal(work.RequestId,result.Value.RequestId);Assert.Single(fixture.Host.Requests.Rows);Assert.Equal(fixture.Source.Id,result.Value.SourceMessageId);
    }
    [Fact]
    public async Task A_followup_keeps_the_conversation_language_when_the_captured_form_used_a_fallback()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        await fixture.Host.Context.Users.UpdateProfileAsync(fixture.Host.Context.Subject.Id,"Fictitious subject","en","America/Argentina/Buenos_Aires",Ct);
        var candidate=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {Mode="Assistant",When="When a followup is needed"};fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[candidate]};
        var work=await fixture.PrepareAsync();Assert.NotNull(work.Operation);Assert.Equal("en",work.Operation.Context.Culture);Assert.Equal("en",fixture.Host.Requests.Rows[0].CapturedCulture);
        Assert.Equal("es",fixture.Host.Forms.Prefill.Value.Culture);
    }
    [Fact]
    public async Task A_temporarily_unavailable_configured_owner_keeps_the_capture_recoverable()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);fixture.Host.Forms.Suggestions=WhatsAppAssistantErrors.Unavailable;
        var result=await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id,Ct);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);Assert.Empty(fixture.Host.Requests.Rows);Assert.Null(fixture.Source.AssistantProcessedAtUtc);
    }
}
