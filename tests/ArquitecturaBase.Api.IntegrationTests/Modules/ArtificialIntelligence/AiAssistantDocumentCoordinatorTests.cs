using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

// ReadDocument uses the same coordinator: one reserved step, the same independent closing of a known result and the same failures.
public sealed class AiAssistantDocumentCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_reading_uses_the_same_reserved_step_and_independent_known_result_closure(bool stops)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, stops ? caller.Cancel : null, read: true);
        await using var provider = Services(fixture);
        var coordinator = new AiAssistantCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiAssistantOptions()), NullLogger<AiAssistantCoordinator>.Instance);

        Assert.True(await coordinator.ProcessNextAsync(caller.Token));

        Assert.Equal((1, 1, 1), (fixture.BeginCalls, fixture.ProviderCalls, fixture.CompleteCalls));
        Assert.Equal("| Monto | $ 1.000 |", fixture.CompletedReading!.Text);
        Assert.Null(fixture.CompletedAudio);
        Assert.Null(fixture.CompletedProposal);
        Assert.Equal((1200L, 300L), (fixture.CompletedUsage!.InputTokens, fixture.CompletedUsage.OutputTokens));
        Assert.False(fixture.CompletionTokenWasCanceled);
        Assert.Equal((0, 0), (fixture.RecordCalls, fixture.FailCalls));
        Assert.False(await coordinator.ProcessNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.ProviderCalls);
    }

    [Theory]
    [InlineData("content", false)]
    [InlineData("uncertain", true)]
    public async Task A_known_failure_closes_the_reserved_reading_step_and_only_a_possible_post_is_uncertain(string failure, bool uncertain)
    {
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null, read: true)
            { ProviderError = failure == "content" ? AiAssistantErrors.ContentUnavailable : AiAssistantErrors.ProviderUncertain };
        await using var provider = Services(fixture);
        var coordinator = new AiAssistantCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiAssistantOptions()), NullLogger<AiAssistantCoordinator>.Instance);

        Assert.True(await coordinator.ProcessNextAsync(TestContext.Current.CancellationToken));

        Assert.Equal(failure == "content" ? AiAssistantErrors.ContentUnavailable.Code : AiAssistantErrors.ProviderUncertain.Code, fixture.FailureCode);
        Assert.Equal(uncertain, fixture.FailureWasUncertain);
        Assert.Equal((1, 0), (fixture.FailCalls, fixture.CompleteCalls));
    }

    private static ServiceProvider Services(AiAssistantProposalCoordinatorFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IAiAssistantDispatchService>(fixture);
        services.AddSingleton<IAiAssistantExecutionService>(fixture);
        services.AddSingleton<IAiAssistantReader>(fixture);
        return services.BuildServiceProvider();
    }
}
