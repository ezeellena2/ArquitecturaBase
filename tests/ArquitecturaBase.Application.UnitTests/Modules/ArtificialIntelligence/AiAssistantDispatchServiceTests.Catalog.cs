using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Channels.SystemQueries;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;
using ArquitecturaBase.Application.UnitTests.TestDoubles.Auth;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace ArquitecturaBase.Application.UnitTests.Modules.ArtificialIntelligence;

public sealed partial class AiAssistantDispatchServiceTests
{
    [Fact]
    public async Task Beginning_a_Home_provider_step_does_not_reserve_unoffered_foreign_sources()
    {
        var host = new Host();
        host.Permissions.Permissions[host.Store.Turn.OwnerUserId] = [ArtificialIntelligencePermissions.AssistantUse, "users.read", "roles.read", "fixture.foreign.read"];
        var limits = new AiAssistantLimits();
        var foreign = new ForeignCatalogSource();
        var catalog = new AiAssistantCapabilityCatalogPolicy([host.Source, foreign], host.Permissions);
        var guard = new AiAssistantAccessGuard(host.Users, host.Permissions, [new(AiProvider.OpenAI, "model", limits.ContextBytes, true, true) {Id="home"}],
            limits, new FakeCurrentUser { UserId = host.Store.Turn.OwnerUserId }, catalog, []);
        var service = new AiAssistantDispatchService(host.Store, host.Work,
            new AiAssistantDispatchGuard(host.Store, host.Store, host.Store, guard, catalog),
            new Protector(), limits, host.Clock, NullLogger<AiAssistantDispatchService>.Instance);
        var result = await service.BeginProviderStepAsync(host.Claim, Ct);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
        Assert.Equal(new[] { "roles.read", "users.read" }, host.Store.Conversation.RequiredPermissions);
        Assert.Equal(0, foreign.Executions);
    }

    private sealed class ForeignCatalogSource : IAssistantCapabilitySource
    {
        public int Executions { get; private set; }
        public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = [new("fixture.foreign", 1,
            AssistantCapabilityKind.Read, "foreign",
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            SystemQueryCapabilityCodec.OutputSchemaJson, new(["fixture.foreign.read"], []), new(4096, 16384, 50, 5))];
        public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
        {
            Executions++;
            throw new InvalidOperationException("Reserving a step cannot execute sources.");
        }
        public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
            throw new InvalidOperationException("Reserving a step cannot parse capability output.");
    }
}
