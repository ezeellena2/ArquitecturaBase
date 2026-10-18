using ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantHomeChannelMutationTests
{
    [Fact]
    public async Task Home_cancel_cannot_mutate_a_neutral_channel_even_when_its_owner_has_the_Home_permission()
    {
        var scenario = new AiAssistantHomeMutationScenario(AiAssistantDispatchServiceTests.CreateNeutralMutationStore);
        var result = await scenario.Service.CancelAsync(new(scenario.Store.Turn.RequestId), TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
        Assert.Null(scenario.Store.Turn.CancelRequestedAtUtc);
    }

    [Fact]
    public async Task Home_remove_cannot_retire_a_neutral_channel_even_when_its_owner_has_the_Home_permission()
    {
        var scenario = new AiAssistantHomeMutationScenario(AiAssistantDispatchServiceTests.CreateNeutralMutationStore);
        var result = await scenario.Service.RemoveConversationAsync(scenario.Store.Conversation.Id, TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.NotFound, result.Error);
        Assert.Equal(AiConversationState.Active, scenario.Store.Conversation.State);
    }
}

public sealed partial class AiAssistantDispatchServiceTests
{
    internal static ObservedReadAssistantStorePorts CreateNeutralMutationStore(Guid actorId, DateTime nowUtc, string channelId)
    {
        var store = new Store(actorId, nowUtc, channelId);
        store.Receipt = AiAssistantRequestReceipt.Create(actorId, store.Turn.RequestId, "fictional-fingerprint", "fixture-key",
            store.Conversation.Id, store.Turn.Id, nowUtc, nowUtc.AddDays(30), channelId, false);
        store.ActiveForConversation = store.Turn;
        return new(store, store, store, store.Conversation, store.Turn, store.Tools);
    }
}
