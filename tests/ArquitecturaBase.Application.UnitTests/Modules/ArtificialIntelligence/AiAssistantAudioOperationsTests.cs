using System.Text.Json;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioOperationsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static AssistantOperationRequest AudioRequest(AssistantOperationsFixture fixture)
    {
        var request = fixture.Request();
        return request with { Text = "", Context = request.Context with {
            SubjectUserId = null, RequiredPermissions = new(["fictional.channel.read"], []), AllowedCapabilities = [], AllowedTerminals = [] },
            Audio = new("audio/private-object", "audio.ogg", "audio/ogg; codecs=opus", 42, new string('a', 64), request.ContentExpiresAtUtc) };
    }

    [Fact]
    public async Task Normalization_accepts_Read_without_Send_Home_tools_or_text_model_qualification()
    {
        var fixture = new AssistantOperationsFixture(qualified: false, audioQualified: true);
        fixture.Permissions.Permissions[fixture.ActorId] = ["fictional.channel.read"];
        var request = AudioRequest(fixture);
        var result = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(result.IsSuccess);
        var turn = Assert.Single(fixture.Store.Turns);
        Assert.Equal("audio-model", turn.Model);
        Assert.Equal("model", fixture.Store.Connection.Model);
        Assert.Equal(1, turn.ConnectionRevision);
        Assert.Equal(request.ContentExpiresAtUtc, turn.ContentExpiresAtUtc);
        Assert.Equal(1, fixture.Work.Commits);
        Assert.Equal(1, fixture.Store.BudgetReads);
        Assert.Equal(0, fixture.Source.Executions);
        var profile = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
        Assert.True(profile.IsSuccess);
        Assert.Equal(AiAssistantPurpose.NormalizeAudio, profile.Value.Purpose);
        Assert.Equal("audio-none-v1", profile.Value.InstructionsId);
        Assert.Equal("fixture-audio", profile.Value.ModelProfileId);
        Assert.Equal(2, profile.Value.ModelProfileVersion);
        Assert.Empty(profile.Value.ChannelContext.AllowedCapabilities);
        Assert.Empty(profile.Value.ChannelContext.AllowedTerminals);
        Assert.DoesNotContain("audio/private-object", turn.ExecutionProfileJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("audio/private-object", turn.ProtectedQuestion!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Audio_expiry_is_the_earliest_source_bound_and_replay_does_not_accept_again()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = AudioRequest(fixture);
        request = request with { Audio = request.Audio! with { ExpiresAtUtc = request.ContentExpiresAtUtc.AddMinutes(-15) } };
        var first = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(first.IsSuccess);
        Assert.Equal(request.Audio!.ExpiresAtUtc, Assert.Single(fixture.Store.Turns).ContentExpiresAtUtc);
        fixture.Store.Connection = fixture.Store.Connection with { Enabled = false, Revision = 2 };
        var repeated = await fixture.Service.SubmitAsync(request, Ct);
        Assert.True(repeated.IsSuccess);
        Assert.Equal(first.Value, repeated.Value);
        Assert.Single(fixture.Store.Receipts);
        Assert.Equal(1, fixture.Store.BudgetReads);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Independent_audio_operations_share_an_actor_and_channel_without_sharing_cancellation(bool running)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var first = AudioRequest(fixture);
        Assert.True((await fixture.Service.SubmitAsync(first, Ct)).IsSuccess);
        var original = Assert.Single(fixture.Store.Turns);
        if (running) Assert.True(original.Claim(Guid.NewGuid(), fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(1)).IsSuccess);
        var second = AudioRequest(fixture) with { Audio = first.Audio! with { StorageKey = "audio/another-private-object" } };

        var accepted = await fixture.Service.SubmitAsync(second, Ct);

        Assert.True(accepted.IsSuccess, accepted.IsFailure ? accepted.Error.Code : null);
        Assert.Equal(2, fixture.Store.Receipts.Count);
        Assert.Equal(2, fixture.Store.Conversations.Count);
        Assert.True((await fixture.Service.CancelAsync(second.Context, second.RequestId, Ct)).IsSuccess);
        Assert.Equal(running ? AiAssistantTurnState.Running : AiAssistantTurnState.Pending, original.State);
        Assert.Null(original.CancelRequestedAtUtc);
        Assert.Equal(AiAssistantTurnState.Canceled, fixture.Store.Turns.Single(turn => turn.RequestId == second.RequestId).State);
        Assert.Equal(0, fixture.Source.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Audio_and_interpretation_can_be_active_together_but_a_second_interpretation_waits(bool audioFirst)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var text = fixture.Request();
        var audio = AudioRequest(fixture);
        foreach (var request in audioFirst ? new[] { audio, text } : new[] { text, audio })
        {
            var result = await fixture.Service.SubmitAsync(request, Ct);
            Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        }

        var blocked = await fixture.Service.SubmitAsync(fixture.Request(), Ct);

        Assert.Equal(AiAssistantErrors.ActiveTurn, blocked.Error);
        Assert.Equal(2, fixture.Store.Receipts.Count);
        Assert.Equal(2, fixture.Store.Turns.Count);
    }

    [Fact]
    public async Task Concurrent_audio_acceptances_keep_the_actor_quota_and_replay_without_consuming_the_text_quota()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var requests = Enumerable.Range(0, 10).Select(_ => AudioRequest(fixture)).ToArray();
        foreach (var request in requests)
            Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var limited = await fixture.Service.SubmitAsync(AudioRequest(fixture), Ct);
        Assert.Equal(AiAssistantErrors.QuotaExceeded(1).Code, limited.Error.Code);
        var budgetReads = fixture.Store.BudgetReads;

        Assert.True((await fixture.Service.SubmitAsync(requests[0], Ct)).IsSuccess);

        Assert.Equal(budgetReads, fixture.Store.BudgetReads);
        Assert.Equal(10, fixture.Store.Receipts.Count);
        Assert.All(fixture.Store.Receipts, receipt => Assert.True(receipt.IsAudio));
        Assert.True((await fixture.Service.SubmitAsync(fixture.Request(), Ct)).IsSuccess);
        Assert.Equal(11, fixture.Store.Receipts.Count);
    }

    [Fact]
    public async Task Audio_still_obeys_the_global_pending_capacity()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var now = fixture.Clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < 50; i++)
        {
            var conversation = AiConversation.Create(fixture.ActorId, now);
            fixture.Store.Conversations.Add(conversation);
            fixture.Store.Turns.Add(AiAssistantTurn.Create(fixture.ActorId, conversation.Id, Guid.NewGuid(), 1, "",
                "en", "UTC", AiProvider.OpenAI, "model", 1, now, now.AddHours(1)));
        }

        var blocked = await fixture.Service.SubmitAsync(AudioRequest(fixture), Ct);

        Assert.Equal(AiAssistantErrors.CapacityLimited(2).Code, blocked.Error.Code);
        Assert.Empty(fixture.Store.Receipts);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("tools")]
    [InlineData("terminal")]
    [InlineData("hash")]
    [InlineData("size")]
    [InlineData("name")]
    public async Task Invalid_audio_inputs_are_rejected_before_any_acceptance(string change)
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = AudioRequest(fixture);
        request = change switch {
            "text" => request with { Text = "Another input" },
            "tools" => request with { Context = request.Context with { AllowedCapabilities = [new("fixture.proposal", 1)] } },
            "terminal" => request with { Context = request.Context with { AllowedTerminals = [new("assistant_proposal", 1)] } },
            "hash" => request with { Audio = request.Audio! with { Sha256 = "bad" } },
            "size" => request with { Audio = request.Audio! with { Size = 25000001 } },
            _ => request with { Audio = request.Audio! with { Name = "../audio.ogg" } },
        };
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsFailure);
        Assert.Empty(fixture.Store.Turns);
        Assert.Equal(0, fixture.Work.Transactions);
    }

    [Fact]
    public async Task Reusing_an_audio_identifier_with_a_different_private_object_conflicts()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = AudioRequest(fixture);
        Assert.True((await fixture.Service.SubmitAsync(request, Ct)).IsSuccess);
        var changed = request with { Audio = request.Audio! with { StorageKey = "audio/other-object" } };
        var result = await fixture.Service.SubmitAsync(changed, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal("Ai.Assistant.RequestReused", result.Error.Code);
        Assert.Single(fixture.Store.Turns);
    }

    [Fact]
    public void Audio_neutral_diagnostics_and_profile_do_not_reveal_content_or_private_paths()
    {
        var fixture = new AssistantOperationsFixture(audioQualified: true);
        var request = AudioRequest(fixture);
        Assert.DoesNotContain(request.Audio!.StorageKey, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(request.Audio.StorageKey, request.Audio.ToString(), StringComparison.Ordinal);
        var response = new AssistantOperationResponse(request.RequestId, "completed", null, null, null) {
            NormalizedAudio = new("private-transcript", ["es"]) };
        Assert.DoesNotContain("private-transcript", response.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("private-transcript", response.NormalizedAudio.ToString(), StringComparison.Ordinal);
        var json = JsonSerializer.Serialize(new AiAssistantExecutionProfileSnapshot("text", 1, "capabilities-v1", 1, fixture.Context));
        Assert.DoesNotContain("Purpose", json, StringComparison.Ordinal);
    }
}
