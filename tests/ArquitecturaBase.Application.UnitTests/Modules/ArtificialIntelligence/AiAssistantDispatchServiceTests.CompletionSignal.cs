using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantDispatchServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completing_a_proposal_notifies_its_opaque_channel_only_after_commit(bool automatic)
    {
        var host = new NeutralHost(automatic);
        var completion = await PrepareProposalAsync(host);
        Assert.Empty(host.CompletionSignal.Channels);
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(3, host.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Completed, host.Store.Turn.State);
            Assert.NotNull(host.Store.Turn.ProtectedAnswer);
        };
        Assert.True((await host.Service.CompleteAsync(host.Claim, completion, Ct)).IsSuccess);
        Assert.Equal(host.Store.Turn.ChannelId, Assert.Single(host.CompletionSignal.Channels));
        Assert.Equal(host.Profile.ChannelContext.ChannelId, host.Store.Turn.ChannelId);
    }

    [Fact]
    public async Task A_text_answer_uses_the_same_postcommit_notification_without_interpreting_the_channel()
    {
        var host = new NeutralHost(false, answerTerminal: true);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var advanced = await host.Execution.AdvanceAsync(host.Context(), new("tools",
            [new("answer", "assistant_answer", "{\"kind\":\"clarification\",\"text\":\"Please clarify your request.\",\"evidenceIds\":[]}")], null, new(4, 2)), Ct);
        Assert.True(advanced.IsSuccess);
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(2, host.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Completed, host.Store.Turn.State);
        };
        Assert.True((await host.Service.CompleteAsync(host.Claim, advanced.Value, Ct)).IsSuccess);
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_or_uncertain_provider_result_wakes_the_consumer_after_the_terminal_commit(bool uncertain)
    {
        var host = new NeutralHost(false);
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(1, host.Work.Commits);
            Assert.Equal(uncertain ? AiAssistantTurnState.Uncertain : AiAssistantTurnState.Failed, host.Store.Turn.State);
        };
        Assert.True((await host.Service.FailAsync(host.Claim, "Fixture.ProviderFailed", uncertain, Ct)).IsSuccess);
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    [Fact]
    public async Task Completion_that_persists_a_revoked_access_failure_still_wakes_its_consumer()
    {
        var host = new NeutralHost(false);
        var completion = await PrepareProposalAsync(host);
        host.Permissions.Permissions[host.ActorId] = [];
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
            Assert.Null(host.Store.Turn.ProtectedAnswer);
        };
        Assert.True((await host.Service.CompleteAsync(host.Claim, completion, Ct)).IsSuccess);
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_failed_commit_never_emits_a_completion_notification(bool failing)
    {
        var host = new NeutralHost(false);
        var completion = failing ? null : await PrepareProposalAsync(host);
        host.Work.CommitFailure = new IOException("Fictitious commit failure.");
        await Assert.ThrowsAsync<IOException>(() => failing
            ? host.Service.FailAsync(host.Claim, "Fixture.ProviderFailed", false, Ct)
            : host.Service.CompleteAsync(host.Claim, completion!, Ct));
        Assert.Equal(1, host.Work.Rollbacks);
        Assert.Empty(host.CompletionSignal.Channels);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_rejected_fence_never_notifies_the_channel(bool failing)
    {
        var host = new NeutralHost(false);
        var completion = failing ? null : await PrepareProposalAsync(host);
        var stale = host.Claim with { Fence = host.Claim.Fence + 1 };
        var result = failing
            ? await host.Service.FailAsync(stale, "Fixture.ProviderFailed", false, Ct)
            : await host.Service.CompleteAsync(stale, completion!, Ct);
        Assert.Equal(AiAssistantErrors.FenceRejected, result.Error);
        Assert.Equal(AiAssistantTurnState.Running, host.Store.Turn.State);
        Assert.Empty(host.CompletionSignal.Channels);
    }

    [Fact]
    public async Task Claim_that_closes_a_pending_operation_after_access_revocation_notifies_after_commit()
    {
        var fixture = new AssistantOperationsFixture();
        Assert.True((await fixture.Service.SubmitAsync(fixture.Request(), Ct)).IsSuccess);
        fixture.Permissions.Permissions[fixture.ActorId] = [];
        var limits = new AiAssistantLimits();
        var catalog = new AiAssistantCapabilityCatalogPolicy([fixture.Source], fixture.Permissions);
        var guard = new AiAssistantAccessGuard(fixture.Users, fixture.Permissions, fixture.Profiles, limits,
            new FakeCurrentUser(), catalog, fixture.AudioProfiles);
        var signal = new RecordingAssistantOperationCompletionSignal();
        var dispatch = new AiAssistantDispatchService(fixture.Store, fixture.Work,
            new(fixture.Store, fixture.Store, fixture.Store, guard, catalog), fixture.Protector, limits, fixture.Clock,
            NullLogger<AiAssistantDispatchService>.Instance, signal);
        signal.BeforeNotify = () =>
        {
            Assert.False(fixture.Work.InTransaction);
            Assert.Equal(2, fixture.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Failed, Assert.Single(fixture.Store.Turns).State);
        };
        var result = await dispatch.ClaimAsync(Ct);
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(fixture.Context.ChannelId, Assert.Single(signal.Channels));
    }

    [Fact]
    public async Task Beginning_a_step_that_commits_cancellation_notifies_after_commit()
    {
        var host = new NeutralHost(false);
        host.Store.Turn.RequestCancellation(host.Clock.GetUtcNow().UtcDateTime);
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(1, host.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Canceled, host.Store.Turn.State);
        };
        var result = await host.Service.BeginProviderStepAsync(host.Claim, Ct);
        Assert.True(result.IsSuccess);
        Assert.False(result.Value);
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    [Fact]
    public async Task Progress_that_commits_an_access_failure_notifies_after_commit()
    {
        var host = new NeutralHost(false);
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        Assert.Empty(host.CompletionSignal.Channels);
        host.Permissions.Permissions[host.ActorId] = [];
        host.CompletionSignal.BeforeNotify = () =>
        {
            Assert.False(host.Work.InTransaction);
            Assert.Equal(2, host.Work.Commits);
            Assert.Equal(AiAssistantTurnState.Failed, host.Store.Turn.State);
        };
        Assert.True((await host.Service.RecordProgressAsync(host.Claim, new(host.Context(), null, [], null), Ct)).IsSuccess);
        Assert.Equal("fixture-manual", Assert.Single(host.CompletionSignal.Channels));
    }

    private static async Task<AiAssistantAdvanceResponse> PrepareProposalAsync(NeutralHost host)
    {
        Assert.True((await host.Service.BeginProviderStepAsync(host.Claim, Ct)).Value);
        var progress = await host.Execution.AdvanceAsync(host.Context(), new("tools",
            [new("proposal", "fixture.proposal", "{}")], null, new(10, 2)), Ct);
        Assert.True(progress.IsSuccess);
        var proposal = Assert.Single(progress.Value.Context.Proposals);
        Assert.True((await host.Service.RecordProgressAsync(host.Claim, progress.Value, Ct)).IsSuccess);
        var selected = await host.Execution.AdvanceAsync(progress.Value.Context, new("tools",
            [new("terminal", "assistant_proposal", System.Text.Json.JsonSerializer.Serialize(new { proposalId = proposal.Id }))], null, new(4, 1)), Ct);
        Assert.True(selected.IsSuccess);
        Assert.NotNull(selected.Value.Proposal);
        return selected.Value;
    }
}
