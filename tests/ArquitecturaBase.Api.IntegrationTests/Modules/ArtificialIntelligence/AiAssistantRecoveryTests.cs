using System.Net;
using ArquitecturaBase.Api.IntegrationTests.Support;
using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

[Collection(ApiTestGroup.Name)]
public sealed class AiAssistantRecoveryTests(ApiFactory factory)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Retrying_result_persistence_does_not_repeat_provider_calls_and_GET_only_reads_confirmed_state()
    {
        using var scenario = await AiAssistantTestScenario.CreateAsync(factory);
        var requestId = Guid.NewGuid();
        await scenario.SubmitAsync(requestId);
        var probe = new AiAssistantCoordinatorProbe();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(probe.Register));
        var operation = host.Services.GetRequiredService<AiAssistantCoordinator>().ProcessNextAsync(Ct);
        await probe.CommitFailed.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        // Wake the injected retry clock after the committed result failed; no provider generation is scheduled again.
        for (var tick = 0; tick < 30 && !operation.IsCompleted; tick++)
        {
            factory.Clock.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(TimeSpan.FromMilliseconds(10), Ct);
        }
        Assert.True(await operation.WaitAsync(TimeSpan.FromSeconds(10), Ct));
        Assert.True(probe.OutsideTransaction);
        Assert.True(probe.DurablyReserved);
        Assert.Equal(2, probe.Calls);
        for (var read = 0; read < 3; read++)
        {
            using var recovered = await scenario.Client.GetAsync($"/api/ai/assistant/turns/{requestId}", Ct);
            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            var turn = await recovered.ReadJsonAsync();
            Assert.Equal("completed", turn.GetProperty("state").GetString());
            Assert.Equal("Usuarios cargados.", turn.GetProperty("answer").GetProperty("text").GetString());
            Assert.Single(turn.GetProperty("evidences").EnumerateArray());
        }
        Assert.Equal(2, probe.Calls);
    }
}
