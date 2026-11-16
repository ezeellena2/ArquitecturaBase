using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// I12, technical: the copy that stops lets the step in progress finish within the drain, gives back what has not reserved a step and
// takes nothing new. No ApiFactory, database or provider: the services are the coordinator fixture and the clock is fake.
public sealed class AiAssistantCoordinatorShutdownTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_stop_during_a_provider_step_lets_the_step_finish_and_the_turn_complete()
    {
        using var stop = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null)
        {
            DuringProviderCall = async token => { await stop.CancelAsync(); await Task.Delay(TimeSpan.FromMilliseconds(20), token); },
        };
        await using var services = Services(fixture);

        Assert.True(await Coordinator(services, clock).ProcessNextAsync(stop.Token, Ct));

        Assert.Equal((1, 1, 0, 0), (fixture.ProviderCalls, fixture.CompleteCalls, fixture.FailCalls, fixture.ReleaseCalls));
        Assert.Equal(fixture.Proposal.Id, fixture.CompletedProposal!.Id);
    }

    [Fact]
    public async Task A_stop_before_any_step_gives_the_turn_back_instead_of_failing_it()
    {
        using var stop = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null) { WhilePreparing = stop.Cancel };
        await using var services = Services(fixture);

        Assert.True(await Coordinator(services, clock).ProcessNextAsync(stop.Token, Ct));

        Assert.Equal((1, 0, 0, 0), (fixture.ReleaseCalls, fixture.BeginCalls, fixture.ProviderCalls, fixture.FailCalls));
    }

    [Fact]
    public async Task The_end_of_the_drain_cuts_the_step_with_the_shutdown_code_and_it_stays_uncertain()
    {
        using var stop = new CancellationTokenSource();
        using var drain = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null)
        {
            DuringProviderCall = async token =>
            {
                await stop.CancelAsync();
                await drain.CancelAsync();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        await using var services = Services(fixture);

        Assert.True(await Coordinator(services, clock).ProcessNextAsync(stop.Token, drain.Token));

        Assert.Equal((AiAssistantErrors.Interrupted.Code, true), (fixture.FailureCode, fixture.FailureWasUncertain));
        Assert.Equal((0, 0), (fixture.CompleteCalls, fixture.ReleaseCalls));
    }

    // I12, review: the stop that cuts the preparation (or the reservation) is not a failure of the turn: it goes back without a warning.
    [Fact]
    public async Task A_stop_that_cuts_the_preparation_gives_the_turn_back_without_a_warning()
    {
        using var stop = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null)
        {
            WhilePreparing = stop.Cancel,
            PrepareFailure = new OperationCanceledException(stop.Token),
        };
        var log = new FakeLogger<AiAssistantCoordinator>();
        await using var services = Services(fixture);

        Assert.True(await Coordinator(services, clock, log).ProcessNextAsync(stop.Token, Ct));

        Assert.Equal((1, 0), (fixture.ReleaseCalls, fixture.FailCalls));
        Assert.DoesNotContain(log.Collector.GetSnapshot(), entry => entry.Level >= LogLevel.Warning);
        Assert.Contains(log.Collector.GetSnapshot(), entry => entry.Message.Contains("given back", StringComparison.Ordinal));
    }

    // I12, review: a release that closes the turn (its person canceled it meanwhile) did not give it back.
    [Fact]
    public async Task A_release_that_closes_the_turn_is_not_logged_as_given_back()
    {
        using var stop = new CancellationTokenSource();
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null) { WhilePreparing = stop.Cancel, ReleaseClosesTheTurn = true };
        var log = new FakeLogger<AiAssistantCoordinator>();
        await using var services = Services(fixture);

        Assert.True(await Coordinator(services, clock, log).ProcessNextAsync(stop.Token, Ct));

        Assert.Equal(1, fixture.ReleaseCalls);
        Assert.DoesNotContain(log.Collector.GetSnapshot(), entry => entry.Message.Contains("given back", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_copy_that_stops_takes_no_new_turn()
    {
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null);
        await using var services = Services(fixture);

        Assert.False(await Coordinator(services, clock).ProcessNextAsync(new CancellationToken(canceled: true), Ct));

        Assert.Equal((0, 0), (fixture.ClaimCalls, fixture.ProviderCalls));
    }

    // The drain starts with the host's stop and lasts its shutdown timeout less the margin to close the turn; meanwhile no lane
    // claims again, and its end cuts the step with the shutdown code.
    [Fact]
    public async Task The_worker_drains_for_the_hosts_shutdown_timeout_less_the_closing_margin_and_claims_nothing_after_the_stop()
    {
        var clock = new FakeTimeProvider();
        var calling = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null)
        {
            DuringProviderCall = async token => { calling.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); },
        };
        await using var services = Services(fixture);
        var options = Options.Create(new AiAssistantOptions { Limits = new() { RunningCapacity = 1 }, CompletionPersistenceSeconds = 5 });
        using var lifetime = new StoppingLifetime();
        var drain = AiAssistantBackgroundService.DrainTime(TimeSpan.FromSeconds(25), 5);
        using var worker = new AiAssistantBackgroundService(Coordinator(services, clock), clock, options,
            Options.Create(new HostOptions { ShutdownTimeout = TimeSpan.FromSeconds(25) }), lifetime, NullLogger<AiAssistantBackgroundService>.Instance);

        await worker.StartAsync(Ct);
        await calling.Task.WaitAsync(TimeSpan.FromSeconds(10), Ct);
        lifetime.StopApplication();
        clock.Advance(drain - TimeSpan.FromSeconds(1));
        await Task.Delay(TimeSpan.FromMilliseconds(100), Ct);
        Assert.Equal(0, fixture.FailCalls);
        clock.Advance(TimeSpan.FromSeconds(1));
        await worker.StopAsync(Ct).WaitAsync(TimeSpan.FromSeconds(10), Ct);

        Assert.Equal(TimeSpan.FromSeconds(18), drain);
        Assert.Equal((AiAssistantErrors.Interrupted.Code, true), (fixture.FailureCode, fixture.FailureWasUncertain));
        Assert.Equal(1, fixture.ClaimCalls);
    }

    [Fact]
    public void The_drain_never_goes_below_zero_and_waits_for_a_host_without_a_shutdown_timeout()
    {
        Assert.Equal(TimeSpan.Zero, AiAssistantBackgroundService.DrainTime(TimeSpan.FromSeconds(5), 5));
        Assert.Equal(Timeout.InfiniteTimeSpan, AiAssistantBackgroundService.DrainTime(Timeout.InfiniteTimeSpan, 5));
    }

    private static ServiceProvider Services(AiAssistantProposalCoordinatorFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAiAssistantDispatchService>(fixture);
        services.AddSingleton<IAiAssistantExecutionService>(fixture);
        services.AddSingleton<IAiAssistantReader>(fixture);
        return services.BuildServiceProvider();
    }

    private static AiAssistantCoordinator Coordinator(ServiceProvider services, FakeTimeProvider clock, ILogger<AiAssistantCoordinator>? logger = null) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), MatchingKeyRing.Instance, clock, Options.Create(new AiAssistantOptions()),
            logger ?? NullLogger<AiAssistantCoordinator>.Instance);

    private sealed class StoppingLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource stopping = new();
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;
        public void StopApplication() => stopping.Cancel();
        public void Dispose() => stopping.Dispose();
    }
}
