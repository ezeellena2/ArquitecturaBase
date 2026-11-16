using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

/// <summary>
/// I4: the deployment that raises a module's capability, with a fictitious version: it registers the new version next to the one the module
/// keeps registered. It is never executed or parsed; a test only seals and compares with it.
/// </summary>
internal sealed class RaisedVersionAssistantSource(string capabilityId, int version) : IAssistantCapabilitySource
{
    private const string Empty = """{"type":"object","properties":{},"required":[],"additionalProperties":false}""";

    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
        [new(capabilityId, version, AssistantCapabilityKind.Read, "Fictitious raised version of a module's capability.", Empty, Empty, new([], []), new(1024, 1024, 1, 5))];

    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<AssistantCapabilityResponse>(AiAssistantErrors.ToolRejected));

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson) => AiAssistantErrors.ToolRejected;
}
