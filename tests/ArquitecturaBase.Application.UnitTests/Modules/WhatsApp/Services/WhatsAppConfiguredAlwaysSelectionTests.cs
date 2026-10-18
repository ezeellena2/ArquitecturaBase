using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.WhatsApp;
using ArquitecturaBase.Domain.Modules.WhatsApp;

namespace ArquitecturaBase.Application.UnitTests.Modules.WhatsApp.Services;

public sealed class WhatsAppConfiguredAlwaysSelectionTests
{
    private static CancellationToken Ct=>TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("cycle")]
    [InlineData("declined")]
    public async Task Automatic_always_skips_an_ineligible_first_candidate_and_sends_the_next_one(string failure)
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var first=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {CreatesAutomaticLoop=failure=="cycle"};
        var second=Alternative(first);
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[first,second]};
        if(failure=="declined")fixture.Host.Reader.DeclinedDefinitions.Add(first.Form.DefinitionId);
        PrepareCandidate(fixture,second);

        var work=await fixture.PrepareAsync();
        Assert.Null(work.Operation);Assert.False(work.ShouldSubmit);
        Assert.Equal(second.SuggestionId,Assert.Single(fixture.Host.Requests.Rows).SuggestionId);
        await fixture.CompleteAsync(work.RequestId);
        Assert.Equal(WhatsAppAssistantRequestState.Sent,fixture.Host.Requests.Rows[0].State);
        Assert.Equal(second.Form.DefinitionId,Assert.Single(fixture.Host.Sessions.Rows).DefinitionId);
        Assert.Single(fixture.Host.Conversation.Outgoing);
        Assert.Equal(0,fixture.Host.Assistant.Submits);Assert.Equal(0,fixture.Host.Assistant.AvailabilityReads);
    }

    [Fact]
    public async Task Only_ineligible_always_candidates_finish_without_an_offer_or_an_AI_operation()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var first=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {CreatesAutomaticLoop=true};
        var second=Alternative(first);
        fixture.Host.Reader.DeclinedDefinitions.Add(second.Form.DefinitionId);
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[first,second]};
        var result=await fixture.Service.PrepareAsync(fixture.Host.Context.Contact.Id,Ct);
        Assert.True(result.IsSuccess);Assert.Null(result.Value);
        Assert.Equal(WhatsAppAssistantRequestState.NoReply,Assert.Single(fixture.Host.Requests.Rows).State);
        Assert.Empty(fixture.Host.Conversation.Outgoing);Assert.Empty(fixture.Host.Sessions.Rows);
        Assert.Equal(0,fixture.Host.Assistant.Submits);Assert.Equal(0,fixture.Host.Assistant.AvailabilityReads);
    }

    [Fact]
    public async Task An_assistant_candidate_remains_available_after_all_always_candidates_are_excluded()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var first=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {CreatesAutomaticLoop=true};
        var assistant=Alternative(first) with {Mode="Assistant",When="When another visit is needed"};
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[first,assistant]};
        var work=await fixture.PrepareAsync();
        Assert.NotNull(work.Operation);Assert.True(work.ShouldSubmit);
        Assert.False(Assert.Single(fixture.Host.Requests.Rows).IsAlways);
        Assert.Empty(fixture.Host.Conversation.Outgoing);Assert.Empty(fixture.Host.Sessions.Rows);
    }

    [Theory]
    [InlineData("cycle")]
    [InlineData("declined")]
    public async Task Manual_always_keeps_the_first_candidate_for_the_humans_decision(string failure)
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(false);
        var first=fixture.Host.Forms.Suggestions.Value.Candidates[0] with {CreatesAutomaticLoop=failure=="cycle"};
        fixture.Host.Forms.Suggestions=fixture.Host.Forms.Suggestions.Value with {Candidates=[first,Alternative(first)]};
        if(failure=="declined")fixture.Host.Reader.DeclinedDefinitions.Add(first.Form.DefinitionId);
        PrepareCandidate(fixture,first);
        var work=await fixture.PrepareAsync();
        Assert.Equal(first.SuggestionId,Assert.Single(fixture.Host.Requests.Rows).SuggestionId);
        await fixture.CompleteAsync(work.RequestId);
        Assert.Equal(WhatsAppAssistantRequestState.Ready,fixture.Host.Requests.Rows[0].State);
        Assert.Empty(fixture.Host.Conversation.Outgoing);Assert.Equal(0,fixture.Host.Assistant.Submits);
    }

    [Fact]
    public async Task A_decline_after_discovery_is_still_checked_before_an_automatic_offer_is_queued()
    {
        var fixture=await WhatsAppConfiguredDispatchFixture.CreateAsync(true);
        var work=await fixture.PrepareAsync();
        fixture.Host.Reader.DeclinedDefinitions.Add(fixture.Host.Forms.Suggestions.Value.Candidates[0].Form.DefinitionId);
        await fixture.CompleteAsync(work.RequestId);
        Assert.Equal(WhatsAppAssistantRequestState.Blocked,Assert.Single(fixture.Host.Requests.Rows).State);
        Assert.Empty(fixture.Host.Conversation.Outgoing);Assert.Empty(fixture.Host.Sessions.Rows);
    }

    private static StructuredFormSuggestionCandidate Alternative(StructuredFormSuggestionCandidate original)=>original with
    {
        SuggestionId=Guid.NewGuid(),CreatesAutomaticLoop=false,
        Form=original.Form with {DefinitionId=Guid.NewGuid(),VersionId=Guid.NewGuid()}
    };

    private static void PrepareCandidate(WhatsAppConfiguredDispatchFixture fixture,StructuredFormSuggestionCandidate candidate)
    {
        var original=fixture.Host.Forms.Suggestion.Value;
        var prefill=original.Prefill with {Form=candidate.Form};
        fixture.Host.Forms.Prefill=prefill;
        fixture.Host.Forms.Suggestion=original with
        {
            Prefill=prefill,CreatesAutomaticLoop=candidate.CreatesAutomaticLoop,
            OriginSuggestion=original.OriginSuggestion with {OriginSuggestionId=candidate.SuggestionId}
        };
    }
}
