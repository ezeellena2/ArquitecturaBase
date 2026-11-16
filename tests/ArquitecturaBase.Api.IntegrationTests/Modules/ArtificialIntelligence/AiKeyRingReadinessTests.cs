using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Results;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I17, review: the verdict of the key ring is kept once it is definitive, for the life of the process; a database that does not answer
// keeps nothing and the next call tries again. Without ApiFactory nor Docker: the comparison itself is the service's, tested apart.
public sealed class AiKeyRingReadinessTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_database_that_does_not_answer_keeps_nothing_and_the_next_check_tries_again()
    {
        var service = new ScriptedControlService(() => throw new TimeoutException("fictional"), () => []);
        var (readiness, check) = Create(service);

        var failed = await check.CheckHealthAsync(new HealthCheckContext(), Ct);
        Assert.Equal(HealthStatus.Unhealthy, failed.Status);
        Assert.Contains(nameof(TimeoutException), failed.Description, StringComparison.Ordinal);
        Assert.Equal(HealthStatus.Healthy, (await check.CheckHealthAsync(new HealthCheckContext(), Ct)).Status);
        Assert.True(await readiness.MatchesAsync(Ct));
        Assert.Equal(2, service.Calls);
    }

    [Fact]
    public async Task A_mismatch_is_kept_for_the_life_of_the_process()
    {
        var service = new ScriptedControlService(() => ["k"], () => []);
        var (readiness, check) = Create(service);

        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext(), Ct)).Status);
        Assert.Equal(HealthStatus.Unhealthy, (await check.CheckHealthAsync(new HealthCheckContext(), Ct)).Status);
        Assert.False(await readiness.MatchesAsync(Ct));
        Assert.Equal(1, service.Calls);
    }

    [Fact]
    public async Task The_worker_and_the_channels_wait_while_the_database_does_not_answer()
    {
        var service = new ScriptedControlService(() => throw new TimeoutException("fictional"), () => []);
        var (readiness, _) = Create(service);

        Assert.False(await readiness.MatchesAsync(Ct));
        Assert.True(await readiness.MatchesAsync(Ct));
        Assert.True(await readiness.MatchesAsync(Ct));
        Assert.Equal(2, service.Calls);
    }

    private static (AiKeyRingReadiness Readiness, AiKeyRingHealthCheck Check) Create(ScriptedControlService service)
    {
        var provider = new ServiceCollection().AddSingleton<IAiKeyRingControlService>(service).BuildServiceProvider();
        var readiness = new AiKeyRingReadiness(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AiKeyRingReadiness>.Instance);
        return (readiness, new AiKeyRingHealthCheck(readiness));
    }

    // Answers each check with the next step, and repeats the last one.
    private sealed class ScriptedControlService(params Func<IReadOnlyList<string>>[] steps) : IAiKeyRingControlService
    {
        public int Calls { get; private set; }

        public Task<Result<IReadOnlyList<string>>> CheckAsync(CancellationToken cancellationToken)
        {
            var step = steps[Math.Min(Calls, steps.Length - 1)];
            Calls++;
            return Task.FromResult(Result.Success(step()));
        }
    }
}
