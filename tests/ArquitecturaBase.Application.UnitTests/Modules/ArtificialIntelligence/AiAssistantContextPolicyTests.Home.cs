using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Time.Testing;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantContextPolicyTests
{
    private static readonly string[] HomeQueryIds = ["users.summary", "roles.list", "roles.permissions", "system.help", "business.information"];

    [Fact]
    public async Task Bootstrap_exposes_only_the_five_Home_queries_even_when_a_foreign_source_is_installed()
    {
        var host = new Host("en");
        var source = new BootstrapCatalogSource([.. HomeQueryIds, "fixture.foreign-proposal"]);
        var catalog = new AiAssistantCapabilityCatalogPolicy([source], host.Permissions);
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(host.Users, host.Permissions,
            [new(AiProvider.OpenAI, "model", limits.ContextBytes, true, true)], limits,
            new FakeCurrentUser { UserId = host.UserId }, catalog, []);
        var reads = new AiAssistantReadPolicy(host.Reader, new ConnectionReader(), null!, host.Permissions, guard,
            new AiAssistantResponsePolicy(host.Protector, new FakeTimeProvider(), catalog), catalog, limits);
        var result = await reads.BootstrapAsync(TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccess);
        Assert.Equal(HomeQueryIds.Order(StringComparer.Ordinal), result.Value.Suggestions.Select(item => item.Id).Order(StringComparer.Ordinal));
        Assert.Equal(0, source.Executions);
    }

    [Fact]
    public async Task Bootstrap_still_requires_the_Home_actors_own_permission()
    {
        var host = new Host("es");
        host.Permissions.Permissions[host.UserId] = [];
        var source = new BootstrapCatalogSource(HomeQueryIds);
        var catalog = new AiAssistantCapabilityCatalogPolicy([source], host.Permissions);
        var limits = new AiAssistantLimits();
        var guard = new AiAssistantAccessGuard(host.Users, host.Permissions,
            [new(AiProvider.OpenAI, "model", limits.ContextBytes, true, true)], limits,
            new FakeCurrentUser { UserId = host.UserId }, catalog, []);
        var reads = new AiAssistantReadPolicy(host.Reader, new ConnectionReader(), null!, host.Permissions, guard,
            new AiAssistantResponsePolicy(host.Protector, new FakeTimeProvider(), catalog), catalog, limits);
        var result = await reads.BootstrapAsync(TestContext.Current.CancellationToken);
        Assert.Equal(AiAssistantErrors.Forbidden, result.Error);
        Assert.Equal(0, source.Executions);
    }

    private sealed class BootstrapCatalogSource(IReadOnlyList<string> ids) : IAssistantCapabilitySource
    {
        public int Executions { get; private set; }
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = ids.Select(id => new AssistantCapabilityDefinition(id, 1,
            id == "fixture.foreign-proposal" ? AssistantCapabilityKind.Proposal : AssistantCapabilityKind.Read, "fixture",
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            SystemQueryCapabilityCodec.OutputSchemaJson, new([], []), new(4096, 16384, 50, 5))).ToArray();
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
        {
            Executions++;
            throw new InvalidOperationException("Bootstrap must only inspect metadata.");
        }
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            throw new InvalidOperationException("Bootstrap cannot parse capability output.");
    }
}
