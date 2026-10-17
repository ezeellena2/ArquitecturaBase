using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// Technical coordinator test: no ApiFactory, collection fixture, database, Redis or provider.
public sealed class AiAssistantProposalCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_selected_proposal_completes_through_the_existing_coordinator_even_when_the_caller_stops(bool callerStops)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, callerStops ? caller.Cancel : null);
        var services = new ServiceCollection();
        services.AddSingleton<IAiAssistantDispatchService>(fixture);
        services.AddSingleton<IAiAssistantExecutionService>(fixture);
        services.AddSingleton<IAiAssistantReader>(fixture);
        await using var provider = services.BuildServiceProvider();
        var coordinator = new AiAssistantCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiAssistantOptions()), NullLogger<AiAssistantCoordinator>.Instance);
        Assert.True(await coordinator.ProcessNextAsync(caller.Token));
        Assert.Equal(1, fixture.ProviderCalls);
        Assert.Equal(1, fixture.BeginCalls);
        Assert.Equal(1, fixture.CompleteCalls);
        Assert.Equal(0, fixture.RecordCalls);
        Assert.Equal(0, fixture.FailCalls);
        Assert.Equal(fixture.Proposal.Id, fixture.CompletedProposal!.Id);
        Assert.False(fixture.CompletionTokenWasCanceled);
        Assert.False(await coordinator.ProcessNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.ProviderCalls);
    }
}
