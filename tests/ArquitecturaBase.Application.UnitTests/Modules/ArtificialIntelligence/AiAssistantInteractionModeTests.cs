using System.Text.Json;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantInteractionModeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Existing_unspecified_context_keeps_its_JSON_and_known_modes_roundtrip()
    {
        var f = new AssistantOperationsFixture();
        var json = JsonSerializer.Serialize(f.Context, JsonSerializerOptions.Web);
        Assert.DoesNotContain("interactionMode", json, StringComparison.Ordinal);
        Assert.Equal(AssistantInteractionMode.Unspecified,
            JsonSerializer.Deserialize<AssistantChannelContext>(json, JsonSerializerOptions.Web)!.InteractionMode);
        foreach (var mode in new[] { AssistantInteractionMode.Assistant, AssistantInteractionMode.Manual })
        {
            var captured = JsonSerializer.Deserialize<AssistantChannelContext>(
                JsonSerializer.Serialize(f.Context with { InteractionMode = mode }, JsonSerializerOptions.Web), JsonSerializerOptions.Web)!;
            Assert.Equal(mode, captured.InteractionMode);
            Assert.Equal(f.Context.ChannelId, captured.ChannelId);
        }
    }

    [Theory]
    [InlineData(AssistantInteractionMode.Assistant)]
    [InlineData(AssistantInteractionMode.Manual)]
    public async Task Recovery_cannot_change_the_captured_interaction_mode_before_decryption(AssistantInteractionMode mode)
    {
        var f = new AssistantOperationsFixture();
        var completed = f.SeedCompleted();
        var result = await f.Service.GetAsync(f.Context with { InteractionMode = mode }, completed.Turn.RequestId, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
        Assert.Equal(0, f.Protector.Reads);
    }

    [Theory]
    [InlineData(AssistantInteractionMode.Assistant)]
    [InlineData(AssistantInteractionMode.Manual)]
    public async Task Cancellation_cannot_change_the_captured_interaction_mode(AssistantInteractionMode mode)
    {
        var f = new AssistantOperationsFixture();
        var request = f.Request();
        Assert.True((await f.Service.SubmitAsync(request, Ct)).IsSuccess);
        var result = await f.Service.CancelAsync(request.Context with { InteractionMode = mode }, request.RequestId, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
        Assert.Null(Assert.Single(f.Store.Turns).CancelRequestedAtUtc);
    }

    [Fact]
    public async Task An_undefined_interaction_mode_is_rejected_before_accepting_work()
    {
        var f = new AssistantOperationsFixture();
        var request = f.Request();
        var result = await f.Service.SubmitAsync(request with { Context = request.Context with { InteractionMode = (AssistantInteractionMode)99 } }, Ct);
        Assert.True(result.IsFailure);
        Assert.Equal(AiAssistantErrors.Invalid, result.Error);
        Assert.Empty(f.Store.Turns);
        Assert.Equal(0, f.Work.Transactions);
    }
}