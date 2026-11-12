using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// Casos del asistente: a channel declares reads that the engine runs before the first step. They go at the start of the request as
// data, never as tools, and are observed reads recorded with the first step.
public sealed class AiAssistantPreloadTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AssistantCapabilityReference[] References(params AssistantCapabilityDefinition[] definitions) =>
        definitions.Select(definition => new AssistantCapabilityReference(definition.Id, definition.Version)).ToArray();

    private static AssistantChannelContext Channel(Guid actor, AssistantCapabilityReference[] allowed, AssistantCapabilityReference[]? preloaded) =>
        new("fixture-preload", actor, actor, "es", "UTC", new([], []), allowed, [new("assistant_proposal", 1)])
            { InteractionMode = AssistantInteractionMode.Assistant, PreloadedCapabilities = preloaded };

    private static ObservedReadAssistantScenario Scenario(bool home = false) =>
        new(AiAssistantDispatchServiceTests.CreateObservedReadStore, home, preloadRead: true);

    private static AiAssistantExecutionContextResponse Asked(ObservedReadAssistantScenario scenario)
    {
        var context = scenario.Context();
        return context with { ProviderRequest = context.ProviderRequest with { Messages = [new("user", "Fictional resource request")] } };
    }

    private static LlmConversationResponse ProposalCall(ObservedReadAssistantScenario scenario, string callId = "proposal-call") => new("tools",
        [new(callId, ObservedReadAssistantSource.ProposalId, JsonSerializer.Serialize(new { resourceId = scenario.Source.ResourceId }))], null, null);

    [Fact]
    public async Task Preloaded_reads_go_apart_from_the_tools_in_their_order_and_do_not_count_toward_the_sixteen_definitions()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [];
        var tools = Enumerable.Range(0, 15).Select(index => PreparedAssistantCapabilitySource.Definition($"fictional.tool-{index}", 1, AssistantCapabilityKind.Read)).ToArray();
        var preloads = new[] { "fictional.preload-c", "fictional.preload-a", "fictional.preload-b" }
            .Select(id => PreparedAssistantCapabilitySource.Definition(id, 1, AssistantCapabilityKind.Read)).ToArray();
        var source = new PreparedAssistantCapabilitySource([.. tools, .. preloads]);
        var result = await new AiAssistantCapabilityCatalogPolicy([source], permissions).BuildAsync(Channel(actor, References(tools), References(preloads)), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(15, result.Value.Capabilities.Count);
        Assert.Equal(preloads.Select(definition => definition.Id), result.Value.Preloaded.Select(definition => definition.Id));
        Assert.Equal(0, source.Calls);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("proposal")]
    [InlineData("repeated")]
    [InlineData("also-a-tool")]
    [InlineData("four")]
    public async Task Only_known_reads_can_be_preloaded_once_apart_from_the_tools_and_at_most_three(string variant)
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [];
        var reads = Enumerable.Range(0, 4).Select(index => PreparedAssistantCapabilitySource.Definition($"fictional.read-{index}", 1, AssistantCapabilityKind.Read)).ToArray();
        var proposal = PreparedAssistantCapabilitySource.Definition("fictional.proposal", 1, AssistantCapabilityKind.Proposal);
        var source = new PreparedAssistantCapabilitySource([.. reads, proposal]);
        AssistantCapabilityReference[] allowed = variant == "also-a-tool" ? References(reads[0]) : References(reads[3]);
        var preloaded = variant switch
        {
            "unknown" => [new("fictional.missing", 1)],
            "proposal" => References(proposal),
            "repeated" => References(reads[0], reads[0]),
            "also-a-tool" => References(reads[0]),
            _ => References(reads),
        };
        var result = await new AiAssistantCapabilityCatalogPolicy([source], permissions).BuildAsync(Channel(actor, allowed, preloaded), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, result.Error);
    }

    [Fact]
    public async Task A_preload_its_identities_cannot_read_is_left_out_and_the_others_reserve_their_permissions()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = ["fictional.read"];
        var readable = PreparedAssistantCapabilitySource.Definition("fictional.readable", 1, AssistantCapabilityKind.Read, "fictional.read");
        var hidden = PreparedAssistantCapabilitySource.Definition("fictional.hidden", 1, AssistantCapabilityKind.Read, "fictional.missing");
        var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([readable, hidden])], permissions);
        var result = await catalog.BuildAsync(Channel(actor, [], References(hidden, readable)), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal("fictional.readable", Assert.Single(result.Value.Preloaded).Id);
        Assert.Equal(new[] { "fictional.read" }, result.Value.RequiredPermissions.Actor);
    }

    // With the agent and a document, the preload leaves out the form candidates, as the tools do.
    [Fact]
    public async Task A_preload_follows_the_turns_file_like_a_tool()
    {
        var actor = Guid.NewGuid();
        var permissions = new FakePermissionService();
        permissions.Permissions[actor] = [];
        var sheet = PreparedAssistantCapabilitySource.Definition("fictional.sheet-read", 1, AssistantCapabilityKind.Read) with { Attachment = AssistantCapabilityAttachment.Spreadsheet };
        var text = PreparedAssistantCapabilitySource.Definition("fictional.text-read", 1, AssistantCapabilityKind.Read) with { Attachment = AssistantCapabilityAttachment.NoDocument };
        var catalog = new AiAssistantCapabilityCatalogPolicy([new PreparedAssistantCapabilitySource([sheet, text])], permissions);
        var context = Channel(actor, [], References(sheet, text));
        Assert.Equal(new[] { "fictional.text-read" }, (await catalog.BuildAsync(context, AssistantAttachmentKind.None, Ct)).Value.Preloaded.Select(definition => definition.Id));
        Assert.Equal(new[] { "fictional.sheet-read", "fictional.text-read" },
            (await catalog.BuildAsync(context, AssistantAttachmentKind.Spreadsheet, Ct)).Value.Preloaded.Select(definition => definition.Id));
        Assert.Empty((await catalog.BuildAsync(context, AssistantAttachmentKind.Document, Ct)).Value.Preloaded);
    }

    [Fact]
    public async Task Capturing_a_turn_seals_the_preloads_it_offers_and_nothing_when_there_are_none()
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore);
        var connection = new AiConnectionRow(AiProvider.OpenAI, "model", "cipher-key", true, 1);
        var actor = scenario.Claim.OwnerUserId;
        var channel = Channel(actor, [new(ObservedReadAssistantSource.ProposalId, 1)], [new(ObservedReadAssistantSource.ReadId, 1)]);
        var now = scenario.Clock.GetUtcNow().UtcDateTime;
        var captured = await scenario.Guard.CaptureOperationProfileAsync(connection, channel, now, AssistantAttachmentKind.None, Ct);
        Assert.True(captured.IsSuccess);
        Assert.Equal(new[] { new AssistantCapabilityReference(ObservedReadAssistantSource.ReadId, 1) }, captured.Value.ChannelContext.PreloadedCapabilities);
        Assert.Equal(new[] { new AssistantCapabilityReference(ObservedReadAssistantSource.ProposalId, 1) }, captured.Value.ChannelContext.AllowedCapabilities);
        Assert.Contains("preloadedCapabilities", JsonSerializer.Serialize(captured.Value, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
        var plain = await scenario.Guard.CaptureOperationProfileAsync(connection, channel with { PreloadedCapabilities = null }, now, AssistantAttachmentKind.None, Ct);
        Assert.True(plain.IsSuccess);
        Assert.Null(plain.Value.ChannelContext.PreloadedCapabilities);
        Assert.DoesNotContain("preloadedCapabilities", JsonSerializer.Serialize(plain.Value, AiAssistantResponsePolicy.JsonOptions), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_engine_runs_each_preload_once_before_the_first_step_and_sends_it_first_as_data_with_its_description()
    {
        var scenario = Scenario();
        var prepared = await scenario.Execution.PreloadAsync(Asked(scenario), Ct);
        Assert.True(prepared.IsSuccess);
        var context = prepared.Value;
        Assert.Equal(1, scenario.Source.ReadExecutions);
        var read = Assert.Single(context.ObservedReads);
        Assert.Equal((scenario.Claim.TurnId, 1, new AssistantCapabilityReference(ObservedReadAssistantSource.ReadId, 1)), (read.TurnId, read.Ordinal, read.Capability));
        var tool = Assert.Single(context.ObservedTools);
        Assert.Same(tool, Assert.Single(context.PreloadedTools));
        Assert.Equal((1, ObservedReadAssistantSource.ReadId, "complete", AssistantCapabilityKind.Read), (tool.Ordinal, tool.ToolId, tool.State, tool.Kind));
        Assert.Equal(0, context.ToolCalls);
        Assert.Equal(tool.Bytes, context.ToolBytes);
        Assert.Equal(2, context.ProviderRequest.Messages.Count);
        var first = context.ProviderRequest.Messages[0];
        Assert.Equal("user", first.Role);
        Assert.StartsWith(AssistantTexts.Get("Prompt.PreloadedReads", scenario.Store.Turn.Culture), first.Text, StringComparison.Ordinal);
        Assert.Contains("Read fictional resource metadata", first.Text, StringComparison.Ordinal);
        Assert.Contains(scenario.Source.CurrentName, first.Text, StringComparison.Ordinal);
        Assert.Equal("Fictional resource request", context.ProviderRequest.Messages[1].Text);
        Assert.DoesNotContain(context.ProviderRequest.Tools, definition => definition.Name == ObservedReadAssistantSource.ReadId);
        Assert.Empty(scenario.Store.Tools);
    }

    [Fact]
    public async Task A_preloaded_read_authorizes_a_proposal_in_the_first_step_and_is_recorded_with_it()
    {
        var scenario = Scenario();
        var context = (await scenario.Execution.PreloadAsync(Asked(scenario), Ct)).Value;
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var proposed = await scenario.Execution.AdvanceAsync(context, ProposalCall(scenario), Ct);
        Assert.True(proposed.IsSuccess);
        Assert.Equal(new[] { 1, 2 }, proposed.Value.NewTools.Select(tool => tool.Ordinal));
        Assert.Empty(proposed.Value.Context.PreloadedTools);
        var proposal = Assert.Single(proposed.Value.Context.Proposals);
        Assert.Equal(1, Assert.Single(proposal.ObservedReads).Ordinal);
        Assert.Equal(1, proposed.Value.Context.ToolCalls);
        Assert.True((await scenario.Dispatch.RecordProgressAsync(scenario.Claim, proposed.Value, Ct)).IsSuccess);
        Assert.Equal(new[] { 1, 2 }, scenario.Store.Tools.Select(tool => tool.Ordinal));
        Assert.Equal(1, scenario.Store.Turn.ToolCalls);
        Assert.Equal(proposed.Value.Context.ToolBytes, scenario.Store.Turn.ToolBytes);
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var selected = await scenario.Execution.AdvanceAsync(proposed.Value.Context,
            new("tools", [new("select-call", "assistant_proposal", JsonSerializer.Serialize(new { proposalId = proposal.Id }))], null, null), Ct);
        Assert.True(selected.IsSuccess);
        Assert.Empty(selected.Value.NewTools);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, selected.Value, Ct)).IsSuccess);
        var mapped = scenario.ReloadedPolicy(new ObservedReadAssistantSource()).Map(scenario.Row(), true, 1);
        Assert.True(mapped.IsSuccess);
        Assert.Equal(proposal.Id, mapped.Value.ObservedProposal!.Id);
        Assert.Equal(1, Assert.Single(mapped.Value.ObservedProposal.ObservedReads).Ordinal);
    }

    [Fact]
    public async Task A_terminal_in_the_first_step_still_records_the_preloads()
    {
        var scenario = Scenario(home: true);
        var context = (await scenario.Execution.PreloadAsync(Asked(scenario), Ct)).Value;
        Assert.True((await scenario.Dispatch.BeginProviderStepAsync(scenario.Claim, Ct)).Value);
        var answered = await scenario.Execution.AdvanceAsync(context, new("tools", [new("answer-call", "assistant_answer",
            """{"kind":"clarification","text":"¿Qué recurso querés revisar?","evidenceIds":[],"proposalId":null}""")], null, null), Ct);
        Assert.True(answered.IsSuccess);
        Assert.Equal(1, Assert.Single(answered.Value.NewTools).Ordinal);
        Assert.True((await scenario.Dispatch.CompleteAsync(scenario.Claim, answered.Value, Ct)).IsSuccess);
        Assert.Equal(1, Assert.Single(scenario.Store.Tools).Ordinal);
        Assert.Equal(AiAssistantTurnState.Completed, scenario.Store.Turn.State);
    }

    [Fact]
    public async Task A_preload_that_cannot_be_read_now_goes_as_unavailable_and_authorizes_nothing()
    {
        var scenario = Scenario();
        scenario.Source.ReadFailuresRemaining = 1;
        var prepared = await scenario.Execution.PreloadAsync(Asked(scenario), Ct);
        Assert.True(prepared.IsSuccess);
        Assert.Empty(prepared.Value.ObservedReads);
        Assert.Equal("unavailable", Assert.Single(prepared.Value.PreloadedTools).State);
        Assert.Contains("\"unavailable\"", prepared.Value.ProviderRequest.Messages[0].Text, StringComparison.Ordinal);
        var proposed = await scenario.Execution.AdvanceAsync(prepared.Value, ProposalCall(scenario), Ct);
        Assert.Equal(AiAssistantErrors.ToolRejected, proposed.Error);
    }

    [Fact]
    public async Task A_preload_refused_for_its_permissions_fails_the_preparation()
    {
        var scenario = Scenario();
        scenario.Source.ReadFailuresRemaining = 1;
        scenario.Source.ReadError = AiAssistantErrors.Forbidden;
        Assert.Equal(AiAssistantErrors.Forbidden, (await scenario.Execution.PreloadAsync(Asked(scenario), Ct)).Error);
    }

    [Fact]
    public async Task A_preload_over_its_result_budget_fails_the_preparation()
    {
        var scenario = Scenario();
        scenario.Source.CurrentName = new string('x', 20_000);
        Assert.Equal(AiAssistantErrors.LimitExceeded, (await scenario.Execution.PreloadAsync(Asked(scenario), Ct)).Error);
    }

    [Fact]
    public async Task A_turn_without_preloads_is_prepared_unchanged()
    {
        var scenario = new ObservedReadAssistantScenario(AiAssistantDispatchServiceTests.CreateObservedReadStore);
        var context = Asked(scenario);
        var prepared = await scenario.Execution.PreloadAsync(context, Ct);
        Assert.True(prepared.IsSuccess);
        Assert.Same(context, prepared.Value);
        Assert.Equal(0, scenario.Source.ReadExecutions);
    }
}
