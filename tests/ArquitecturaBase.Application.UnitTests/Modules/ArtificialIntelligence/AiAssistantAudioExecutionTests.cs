using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioExecutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    [Fact]
    public async Task Prepare_uses_the_saved_OpenAI_key_with_the_STT_model_without_history_prompts_or_tools()
    {
        var f = new AssistantAudioExecutionFixture();
        var prepared = await f.ContextPolicy.PrepareAsync(f.Claim, Ct);
        Assert.True(prepared.IsSuccess);
        var request = prepared.Value.ProviderRequest;
        Assert.Equal("audio-model", request.Model);
        Assert.Equal("fixture-audio-key", request.ApiKey);
        Assert.Equal(f.Audio, request.Audio);
        Assert.Equal(25000000, request.AudioMaximumBytes);
        Assert.Empty(request.Messages);
        Assert.Empty(request.Tools);
        Assert.Empty(request.SystemPrompt);
        Assert.Equal(0, f.Inner.Source.Executions);
    }
    [Theory]
    [InlineData("revision")]
    [InlineData("pause")]
    [InlineData("profile")]
    [InlineData("permission")]
    [InlineData("expired")]
    [InlineData("hash")]
    public async Task Prepare_rejects_invalid_current_access_connection_profile_or_source_before_transport(string change)
    {
        var f = new AssistantAudioExecutionFixture();
        if (change == "revision") f.Inner.Store.Connection = f.Inner.Store.Connection with { Revision = 2 };
        if (change == "pause") f.Inner.Store.Connection = f.Inner.Store.Connection with { Enabled = false };
        if (change == "profile") f.Inner.AudioProfiles[0] = f.Inner.AudioProfiles[0] with { Version = 3 };
        if (change == "permission") f.Inner.Permissions.Permissions[f.Inner.ActorId] = [];
        if (change == "expired") f.Inner.Clock.Advance(TimeSpan.FromHours(2));
        if (change == "hash") f.ProtectDescriptor(f.Audio with { Sha256 = "bad" });
        Assert.True((await f.ContextPolicy.PrepareAsync(f.Claim, Ct)).IsFailure);
        Assert.Equal(0, f.Client.Calls);
    }
    [Fact]
    public async Task A_normalized_result_is_a_complete_technical_terminal_without_a_model_tool_call()
    {
        var f = new AssistantAudioExecutionFixture();
        var response = new LlmConversationResponse("normalizedAudio", [], null, new(null, null) { AudioSeconds = 2.75m })
            { NormalizedAudio = new("Visit completed", ["en"]) };
        var result = await f.Execution.AdvanceAsync(await f.ContextAsync(Ct), response, Ct);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsComplete);
        Assert.Equal(response.NormalizedAudio, result.Value.NormalizedAudio);
        Assert.Empty(result.Value.NewTools);
        Assert.Null(result.Value.Answer);
        Assert.Null(result.Value.Proposal);
        Assert.Equal(0, f.Inner.Source.Executions);
    }
    [Theory]
    [InlineData("tool")]
    [InlineData("tooLong")]
    [InlineData("nul")]
    [InlineData("languages")]
    public async Task Audio_completion_never_accepts_actions_or_repaired_partial_output(string change)
    {
        var f = new AssistantAudioExecutionFixture();
        var audio = new AssistantNormalizedAudio("Visit", ["en"]);
        if (change == "tooLong") audio = audio with { Text = new string('a', 4001) };
        if (change == "nul") audio = audio with { Text = "invalid\0text" };
        if (change == "languages") audio = audio with { Languages = ["en", "en"] };
        var response = new LlmConversationResponse("normalizedAudio", change == "tool" ? [new("call", "fixture.proposal", "{}")] : [], null, null) { NormalizedAudio = audio };
        Assert.True((await f.Execution.AdvanceAsync(await f.ContextAsync(Ct), response, Ct)).IsFailure);
        Assert.Equal(0, f.Inner.Source.Executions);
    }
    [Fact]
    public async Task A_known_audio_result_is_protected_and_recovered_by_the_same_operation_and_scope()
    {
        var f = new AssistantAudioExecutionFixture();
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var context = await f.ContextAsync(Ct);
        var audio = new AssistantNormalizedAudio("Visité la sucursal Norte", ["es"]);
        var complete = new AiAssistantAdvanceResponse(context, null, [], new(null, null) { AudioSeconds = 2.75m }) { NormalizedAudio = audio };
        Assert.True((await f.Dispatch.CompleteAsync(f.Claim, complete, Ct)).IsSuccess);
        Assert.Equal(AiAssistantTurnState.Completed, f.Turn.State);
        Assert.Equal(2.75m, f.Turn.AudioSeconds);
        Assert.Null(f.Turn.InputTokens);
        Assert.True(f.Turn.UsageUnknown);
        Assert.DoesNotContain(audio.Text, f.Turn.ProtectedAnswer!, StringComparison.Ordinal);
        var result = await f.Inner.Service.GetAsync(f.Profile.ChannelContext, f.Turn.RequestId, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(audio.Text, result.Value.NormalizedAudio!.Text);
        Assert.Equal(["es"], result.Value.NormalizedAudio.Languages);
        Assert.Null(result.Value.Proposal);
        Assert.Equal("audio-none-v1", result.Value.Provenance!.InstructionsId);
        Assert.Equal(2, f.Inner.Work.Commits);
        Assert.Equal(0, f.Inner.Source.Executions);
        Assert.Empty(f.Inner.Store.Tools);
        var mapped = new ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services.AiAssistantResponsePolicy(f.Inner.Protector, f.Inner.Clock, f.Catalog)
            .Map((await f.Inner.Store.FindTurnAsync(f.Turn.Id, Ct))!, true, 0);
        Assert.True(mapped.IsSuccess);
        Assert.DoesNotContain(audio.Text, JsonSerializer.Serialize(mapped.Value), StringComparison.Ordinal);
    }
    [Fact]
    public async Task Late_audio_completion_records_duration_but_never_repopulates_expired_content()
    {
        var f = new AssistantAudioExecutionFixture();
        Assert.True((await f.Dispatch.BeginProviderStepAsync(f.Claim, Ct)).Value);
        var context = await f.ContextAsync(Ct);
        f.Inner.Clock.Advance(TimeSpan.FromHours(2));
        f.Turn.PurgeContent();
        var result = await f.Dispatch.CompleteAsync(f.Claim, new(context, null, [], new(null, null) { AudioSeconds = 2.75m })
            { NormalizedAudio = new("Late private text", ["en"]) }, Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(AiAssistantTurnState.Failed, f.Turn.State);
        Assert.Equal(AiAssistantErrors.Expired.Code, f.Turn.FailureCode);
        Assert.Equal(2.75m, f.Turn.AudioSeconds);
        Assert.Null(f.Turn.ProtectedQuestion);
        Assert.Null(f.Turn.ProtectedAnswer);
        var response = await f.Inner.Service.GetAsync(f.Profile.ChannelContext, f.Turn.RequestId, Ct);
        Assert.True(response.IsSuccess);
        Assert.Null(response.Value.NormalizedAudio);
    }
}
