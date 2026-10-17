using System.Text.Json;
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
