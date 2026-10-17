using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class PreparedAssistantCapabilitySource(IReadOnlyList<AssistantCapabilityDefinition> definitions) : IAssistantCapabilitySource
{
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } = definitions;
    public int Calls { get; private set; }
    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        Calls++;
        throw new InvalidOperationException("Building the catalog must not execute a capability.");
    }
    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) =>
        throw new InvalidOperationException("Building the catalog must not parse content.");

    public static AssistantCapabilityDefinition Definition(string id = "users.summary", int version = 1,
        AssistantCapabilityKind kind = AssistantCapabilityKind.Read, params string[] permissions) =>
        new(id, version, kind, "Fictional bounded capability",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}",
            "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}",
            new(permissions, []), new(4096, 16384, 50, 5));
}

internal sealed record FictionalAssistantProposalPayload(Guid DefinitionId, string PrivateText) : AssistantCapabilityPayload;
