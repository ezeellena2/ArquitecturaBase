using System.Text;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantObservedReadTests
{
    [Fact]
    public async Task A_failed_read_keeps_the_other_call_and_does_not_create_observed_authority()
    {
        var scenario = Scenario(); scenario.Source.ReadFailuresRemaining = 1;
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, read.Value.Context.ToolCalls);
        Assert.Equal(2, read.Value.NewTools.Count);
        Assert.Equal("unavailable", read.Value.NewTools[0].State);
        Assert.Null(read.Value.NewTools[0].Evidence);
        Assert.Null(read.Value.NewTools[0].ProposalId);
        Assert.Equal(2, Assert.Single(read.Value.Context.ObservedReads).Ordinal);
        Assert.Equal(read.Value.Context.ProviderRequest.ToolResults.Sum(result => Encoding.UTF8.GetByteCount(result.ResultJson)), read.Value.Context.ToolBytes);
        Assert.DoesNotContain(scenario.Source.ReadError.Description, read.Value.NewTools[0].ResultJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unavailable_read_does_not_authorize_a_guessed_proposal()
    {
        var scenario = Scenario(); scenario.Source.ReadFailuresRemaining = 1;
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadCall(), Ct);
        Assert.True(read.IsSuccess);
        Assert.Empty(read.Value.Context.ObservedReads);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, ProposalCall(scenario), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, proposal.Error);
    }

    [Fact]
    public async Task Unavailable_progress_is_persisted_and_a_fresh_codec_restores_only_the_successful_read()
    {
        var scenario = Scenario();
        var row = await CompleteAfterUnavailableAsync(scenario);
        Assert.Equal("unavailable", scenario.Store.Tools[0].State);
        var codec = new ObservedReadAssistantSource();
        var mapped = scenario.ReloadedPolicy(codec).Map(row, true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(2, Assert.Single(mapped.Value.Proposal!.ObservedReads).Ordinal);
        Assert.Equal(1, codec.ReadParseCalls);
        Assert.Equal(0, codec.ReadExecutions);
    }

    [Fact]
    public async Task An_injected_unavailable_ordinal_cannot_be_restored_as_proposal_authority()
    {
        var scenario = Scenario();
        var row = await CompleteAfterUnavailableAsync(scenario);
        var tool = row.ToolResults.Single(result => result.Ordinal == 3);
        var stored = scenario.Protector.Unprotect(row.OwnerUserId, row.ConversationId, row.Id, "tool:3", row.PayloadRevision, tool.ProtectedResult!);
        using var document = JsonDocument.Parse(stored!);
        var injected = JsonSerializer.Serialize(new { output = document.RootElement.GetProperty("output"), readOrdinals = new[] { 1 } });
        var ciphertext = scenario.Protector.Protect(row.OwnerUserId, row.ConversationId, row.Id, "tool:3", row.PayloadRevision, injected);
        row = row with { ToolResults = row.ToolResults.Select(result => result.Ordinal == 3 ? result with { ProtectedResult = ciphertext } : result).ToArray() };
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(row, true, 1);
        Assert.Equal(AiAssistantErrors.ContentUnavailable, mapped.Error);
    }

    [Fact]
    public async Task A_read_authorization_failure_still_stops_the_turn()
    {
        var scenario = Scenario(); scenario.Source.ReadFailuresRemaining = 1; scenario.Source.ReadError = AiAssistantErrors.Forbidden;
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.Equal(AiAssistantErrors.Forbidden, read.Error);
        Assert.Equal(1, scenario.Source.ReadExecutions);
    }

    [Fact]
    public async Task Caller_cancellation_during_a_read_is_never_an_unavailable_result()
    {
        var scenario = Scenario(); scenario.Source.ReadFailuresRemaining = 1;
        using var cancelled = new CancellationTokenSource(); scenario.Source.OnRead = cancelled.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), cancelled.Token));
        Assert.Equal(1, scenario.Source.ReadExecutions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_transient_read_exception_is_an_unavailable_tool_without_private_error_content(bool timeout)
    {
        var scenario = Scenario();
        scenario.Source.OnRead = () => { if (timeout) throw new TimeoutException("private endpoint details"); throw new IOException("private storage details"); };
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, read.Value.NewTools.Count);
        Assert.All(read.Value.NewTools, tool => Assert.Equal("unavailable", tool.State));
        Assert.All(read.Value.NewTools, tool => Assert.DoesNotContain("private", tool.ResultJson, StringComparison.Ordinal));
        Assert.Empty(read.Value.Context.ObservedReads);
    }

    [Fact]
    public async Task The_global_deadline_during_a_read_does_not_continue_to_the_second_call()
    {
        var scenario = Scenario();
        scenario.Source.OnRead = () => scenario.Clock.Advance(TimeSpan.FromMinutes(2));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct));
        Assert.Equal(1, scenario.Source.ReadExecutions);
    }
    [Fact]
    public async Task A_read_local_timeout_keeps_the_turn_available_for_the_other_call()
    {
        var scenario = Scenario();
        scenario.Source.OnRead = () => scenario.Clock.Advance(TimeSpan.FromSeconds(6));
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.True(read.IsSuccess);
        Assert.Equal(2, read.Value.NewTools.Count);
        Assert.All(read.Value.NewTools, tool => Assert.Equal("unavailable", tool.State));
        Assert.Empty(read.Value.Context.ObservedReads);
        Assert.Equal(2, scenario.Source.ReadExecutions);
    }
    [Fact]
    public async Task Invalid_read_arguments_remain_a_fatal_validation_failure()
    {
        var scenario = Scenario(); scenario.Source.ReadFailuresRemaining = 1; scenario.Source.ReadError = AiAssistantErrors.Invalid;
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.Equal(AiAssistantErrors.Invalid, read.Error);
        Assert.Equal(1, scenario.Source.ReadExecutions);
    }
    private static LlmConversationResponse ReadBatch() => new("tools",
        [new("failed-read", ObservedReadAssistantSource.ReadId, "{}"), new("successful-read", ObservedReadAssistantSource.ReadId, "{}")],
        "fictional-continuation", null);

    private static async Task<AiAssistantTurnRow> CompleteAfterUnavailableAsync(ObservedReadAssistantScenario scenario)
    {
        scenario.Source.ReadFailuresRemaining = 1;
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var read = await scenario.Execution.AdvanceAsync(scenario.Context(), ReadBatch(), Ct);
        Assert.True(read.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, read.Value, Ct)).IsSuccess);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposal = await scenario.Execution.AdvanceAsync(read.Value.Context, ProposalCall(scenario), Ct);
        Assert.True(proposal.IsSuccess);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposal.Value, Ct)).IsSuccess);
        var selected = Assert.Single(proposal.Value.Context.Proposals);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var terminal = await scenario.Execution.AdvanceAsync(proposal.Value.Context,
            new("tools", [new("terminal", "assistant_proposal", JsonSerializer.Serialize(new { proposalId = selected.Id }))], null, null), Ct);
        Assert.True(terminal.IsSuccess);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, terminal.Value, Ct)).IsSuccess);
        return scenario.Row();
    }
}
