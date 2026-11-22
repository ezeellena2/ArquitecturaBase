using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Channels;
using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I4, the provider: a provider a newer version saved reads as Unsupported. The connection shows none of its values, only its revision and
// the flag, and the worker leaves a turn sealed with it for the version that knows it.
public sealed class AiConnectionUnsupportedProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_connection_with_a_provider_this_version_does_not_know_shows_empty_and_paused_with_its_revision()
    {
        var shown = new AiConnectionRow(AiProviders.Unsupported, "future-model", "cipher-key", false, 7).ToResponse(30);
        var known = new AiConnectionRow(AiProvider.Anthropic, "model", "cipher-key", true, 7).ToResponse(30);

        Assert.Equal((AiProvider.OpenAI, "", false, false, 7L, true), (shown.Provider, shown.Model, shown.HasCredential, shown.Enabled, shown.Revision, shown.UnsupportedProvider));
        Assert.Equal((AiProvider.Anthropic, "model", true, true, false), (known.Provider, known.Model, known.HasCredential, known.Enabled, known.UnsupportedProvider));
        Assert.False(AiProviders.IsSupported(AiProviders.Unsupported));
    }

    [Fact]
    public async Task The_claim_leaves_a_turn_with_a_provider_it_does_not_know_pending_and_takes_the_next()
    {
        var f = new AssistantOperationsFixture();
        var future = f.Request();
        var known = f.Request() with { Context = f.Context with { ChannelConversationId = Guid.NewGuid() } };
        Assert.True((await f.Service.SubmitAsync(future, Ct)).IsSuccess);
        f.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True((await f.Service.SubmitAsync(known, Ct)).IsSuccess);
        var turn = f.Store.Turns.Single(row => row.RequestId == future.RequestId);
        typeof(AiAssistantTurn).GetProperty(nameof(AiAssistantTurn.Provider))!.SetValue(turn, AiProviders.Unsupported);
        var dispatch = new AiAssistantDispatchService(f.Store, f.Work, new AiAssistantDispatchGuard(f.Store, f.Store, f.Store, f.Guard, f.Catalog),
            f.Protector, f.Limits, f.Clock, NullLogger<AiAssistantDispatchService>.Instance, new RecordingAssistantOperationCompletionSignal());

        var claimed = await dispatch.ClaimAsync(Ct);

        Assert.Equal(known.RequestId, claimed.Value?.RequestId);
        Assert.Equal(AiAssistantTurnState.Pending, turn.State);
    }
}
