using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Users;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

// I10: the four lanes and the reconciliation ask every second whether there is work. Asking without work writes nothing in
// Information, so the log keeps what happened; a failure is still logged by the scheduler that asked.
public sealed partial class AiAssistantDispatchServiceTests
{
    [Fact]
    public async Task A_claim_without_work_writes_nothing_in_information()
    {
        var logger = new FakeLogger<AiAssistantDispatchService>();
        var host = new Host(logger: logger);

        var result = await host.Service.ClaimAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Information);
    }

    [Fact]
    public async Task A_reconciliation_without_work_writes_nothing_in_information()
    {
        var host = new Host();
        var logger = new FakeLogger<AiAssistantRetentionService>();
        var retention = new AiAssistantRetentionService(host.Store, host.Work, new AiAssistantLimits(), host.Clock, logger, new InMemoryUserAccounts());

        var result = await retention.ReconcileExpiredBatchAsync(Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value);
        Assert.DoesNotContain(logger.Collector.GetSnapshot(), record => record.Level >= LogLevel.Information);
    }
}
