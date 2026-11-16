using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I19: the claim counts only the turns running, the one number it uses; the day's receipts are the acceptance's quota, and this store
// refuses to count them.
public sealed partial class AiAssistantDispatchServiceTests
{
    [Fact]
    public async Task With_the_running_capacity_full_the_claim_leaves_the_turn_pending_without_counting_receipts()
    {
        var host = new Host(limits: new AiAssistantLimits { RunningCapacity = 1 });
        var waiting = host.AddPending();

        var result = await host.Service.ClaimAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(AiAssistantTurnState.Pending, waiting.State);
    }
}
