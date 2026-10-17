using ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Assistant;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.SystemQueries;
using ArquitecturaBase.Infrastructure.Assistant;
using Microsoft.Extensions.DependencyInjection;

namespace ArquitecturaBase.Api.IntegrationTests.Modules.ArtificialIntelligence;

public sealed class AssistantCapabilityExecutorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Each_execution_resolves_and_disposes_its_own_scope()
    {
        var probe = new AssistantCapabilityExecutorProbe();
        await using var services = Services(probe);
        var executor = services.GetRequiredService<IAssistantCapabilityExecutor>();
        var request = Request(1);
        Assert.True((await executor.ExecuteAsync(request, Ct)).IsSuccess);
        Assert.True((await executor.ExecuteAsync(request, Ct)).IsSuccess);
        Assert.Equal(2, probe.CreatedScopes.Count);
        Assert.Equal(2, probe.CreatedScopes.Distinct().Count());
        Assert.Equal(probe.CreatedScopes, probe.DisposedScopes);
        Assert.Equal(2, probe.Requests.Count);
        Assert.All(probe.Requests, actual => Assert.Same(request, actual));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task The_requested_version_is_passed_to_its_registered_source(int version)
    {
        var probe = new AssistantCapabilityExecutorProbe();
        await using var services = Services(probe);
        var result = await services.GetRequiredService<IAssistantCapabilityExecutor>().ExecuteAsync(Request(version), Ct);
        Assert.True(result.IsSuccess);
        Assert.Equal(version, Assert.Single(probe.Requests).Capability.Version);
        Assert.Equal(probe.CreatedScopes, probe.DisposedScopes);
    }

    [Fact]
    public async Task Two_versions_in_different_sources_select_only_the_requested_version()
    {
        var first = new AssistantCapabilityExecutorProbe { Definitions = [AssistantCapabilityExecutorProbe.Definition(1)] };
        var second = new AssistantCapabilityExecutorProbe { Definitions = [AssistantCapabilityExecutorProbe.Definition(2)] };
        var registrations = new ServiceCollection();
        registrations.AddScoped<IAssistantCapabilitySource>(_ => new AssistantCapabilityExecutorSource(first));
        registrations.AddScoped<IAssistantCapabilitySource>(_ => new AssistantCapabilityExecutorSource(second));
        registrations.AddSingleton<IAssistantCapabilityExecutor, ScopedAssistantCapabilityExecutor>();
        await using var services = registrations.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        Assert.True((await services.GetRequiredService<IAssistantCapabilityExecutor>().ExecuteAsync(Request(2), Ct)).IsSuccess);
        Assert.Empty(first.Requests);
        Assert.Equal(2, Assert.Single(second.Requests).Capability.Version);
        Assert.Single(first.CreatedScopes);
        Assert.Single(second.CreatedScopes);
        Assert.Equal(first.CreatedScopes, first.DisposedScopes);
        Assert.Equal(second.CreatedScopes, second.DisposedScopes);
    }

    [Fact]
    public async Task An_unknown_version_never_falls_back_to_the_latest_registered_version()
    {
        var probe = new AssistantCapabilityExecutorProbe();
        await using var services = Services(probe);
        var result = await services.GetRequiredService<IAssistantCapabilityExecutor>().ExecuteAsync(Request(3), Ct);
        Assert.Equal(SystemQueryErrors.NotFound, result.Error);
        Assert.Empty(probe.Requests);
        Assert.Single(probe.CreatedScopes);
        Assert.Equal(probe.CreatedScopes, probe.DisposedScopes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Duplicate_exact_references_fail_before_any_source_executes(bool twoSources)
    {
        var probe = new AssistantCapabilityExecutorProbe
        {
            Definitions = twoSources
                ? [AssistantCapabilityExecutorProbe.Definition(1)]
                : [AssistantCapabilityExecutorProbe.Definition(1), AssistantCapabilityExecutorProbe.Definition(1)]
        };
        await using var services = Services(probe, twoSources);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            services.GetRequiredService<IAssistantCapabilityExecutor>().ExecuteAsync(Request(1), Ct));
        Assert.Empty(probe.Requests);
        Assert.Equal(twoSources ? 2 : 1, probe.CreatedScopes.Count);
        Assert.Equal(probe.CreatedScopes.Order(), probe.DisposedScopes.Order());
    }

    [Theory]
    [InlineData("knownFailure")]
    [InlineData("exception")]
    [InlineData("canceled")]
    public async Task A_scope_is_disposed_when_the_source_fails_throws_or_is_canceled(string outcome)
    {
        var probe = new AssistantCapabilityExecutorProbe
        {
            ThrowOnExecute = outcome == "exception", FailOnExecute = outcome == "knownFailure"
        };
        await using var services = Services(probe);
        var executor = services.GetRequiredService<IAssistantCapabilityExecutor>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        if (outcome == "canceled")
        {
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.ExecuteAsync(Request(1), cancellation.Token));
        }
        else if (outcome == "exception")
            await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(Request(1), cancellation.Token));
        else
            Assert.Equal(SystemQueryErrors.Forbidden, (await executor.ExecuteAsync(Request(1), cancellation.Token)).Error);
        Assert.Single(probe.Requests);
        Assert.Single(probe.CreatedScopes);
        Assert.Equal(probe.CreatedScopes, probe.DisposedScopes);
    }

    private static ServiceProvider Services(AssistantCapabilityExecutorProbe probe, bool twoSources = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddScoped<IAssistantCapabilitySource, AssistantCapabilityExecutorSource>();
        if (twoSources) services.AddScoped<IAssistantCapabilitySource, AssistantCapabilityExecutorSource>();
        services.AddSingleton<IAssistantCapabilityExecutor, ScopedAssistantCapabilityExecutor>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static AssistantCapabilityRequest Request(int version) => new(Guid.NewGuid(),
        new("fictional-channel", Guid.NewGuid(), null, "es", "UTC", new([], []), [new("fictional.read", version)], []),
        new("fictional.read", version), "{}", []);
}
