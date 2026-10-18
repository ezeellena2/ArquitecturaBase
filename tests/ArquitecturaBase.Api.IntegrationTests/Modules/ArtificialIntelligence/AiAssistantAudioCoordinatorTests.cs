using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AiAssistantAudioCoordinatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Normalized_audio_uses_the_same_reserved_step_and_independent_known_result_closure(bool stops)
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, stops ? caller.Cancel : null, normalize: true);
        var services = new ServiceCollection();
        services.AddSingleton<IAiAssistantDispatchService>(fixture);
        services.AddSingleton<IAiAssistantExecutionService>(fixture);
        services.AddSingleton<IAiAssistantReader>(fixture);
        await using var provider = services.BuildServiceProvider();
        var coordinator = new AiAssistantCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiAssistantOptions()), NullLogger<AiAssistantCoordinator>.Instance);
        Assert.True(await coordinator.ProcessNextAsync(caller.Token));
        Assert.Equal(1, fixture.BeginCalls);
        Assert.Equal(1, fixture.ProviderCalls);
        Assert.Equal(1, fixture.CompleteCalls);
        Assert.Equal("Fictitious visit", fixture.CompletedAudio!.Text);
        Assert.Null(fixture.CompletedProposal);
        Assert.Equal(2.75m, fixture.CompletedUsage!.AudioSeconds);
        Assert.Null(fixture.CompletedUsage.InputTokens);
        Assert.False(fixture.CompletionTokenWasCanceled);
        Assert.Equal(0, fixture.RecordCalls);
        Assert.Equal(0, fixture.FailCalls);
        Assert.False(await coordinator.ProcessNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, fixture.ProviderCalls);
    }
    [Fact]
    public async Task A_known_private_content_failure_closes_the_reserved_audio_step_without_uncertainty()
    {
        var clock = new FakeTimeProvider();
        var fixture = new AiAssistantProposalCoordinatorFixture(clock, null, normalize: true)
            { ProviderError = AiAssistantErrors.ContentUnavailable };
        var services = new ServiceCollection();
        services.AddSingleton<IAiAssistantDispatchService>(fixture);
        services.AddSingleton<IAiAssistantExecutionService>(fixture);
        services.AddSingleton<IAiAssistantReader>(fixture);
        await using var provider = services.BuildServiceProvider();
        var coordinator = new AiAssistantCoordinator(provider.GetRequiredService<IServiceScopeFactory>(), clock,
            Options.Create(new AiAssistantOptions()), NullLogger<AiAssistantCoordinator>.Instance);
        Assert.True(await coordinator.ProcessNextAsync(TestContext.Current.CancellationToken));
        Assert.Equal(AiAssistantErrors.ContentUnavailable.Code, fixture.FailureCode);
        Assert.False(fixture.FailureWasUncertain);
        Assert.Equal(1, fixture.FailCalls);
        Assert.Equal(0, fixture.CompleteCalls);
    }
}
