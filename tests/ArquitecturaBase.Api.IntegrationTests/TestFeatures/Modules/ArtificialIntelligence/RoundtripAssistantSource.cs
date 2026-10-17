using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class RoundtripAssistantSource : IAssistantCapabilitySource
{
    public Guid ResourceId { get; } = Guid.Parse("40000000-0000-0000-0000-000000000007");
    public int Executions { get; private set; }
    public int ParseCalls { get; private set; }
    public AssistantCapabilityRequest? LastRequest { get; private set; }
    public Action<CancellationToken>? BeforeReturn { get; set; }
    public string PrivateText { get; set; } = "fictitious-private-value";
    public int ItemCount { get; set; } = 1;
    public AssistantCapabilityBudget Budget { get; set; } = new(4096, 16384, 5, 5);
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions => [new("fixture.proposal", 1, AssistantCapabilityKind.Proposal, "Fictional typed proposal",
        """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
        """{"type":"object","properties":{"resourceId":{"type":"string"},"privateText":{"type":"string"}},"required":["resourceId","privateText"],"additionalProperties":false}""",
        new([], []), Budget)];
    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        Executions++;
        LastRequest = request;
        BeforeReturn?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Capability != new AssistantCapabilityReference("fixture.proposal", 1) || request.ArgumentsJson != "{}")
            return Task.FromResult<Result<AssistantCapabilityResponse>>(AiAssistantErrors.ToolRejected);
        return Task.FromResult<Result<AssistantCapabilityResponse>>(new AssistantCapabilityResponse("proposed",
            new RoundtripAssistantPayload(ResourceId, PrivateText), [], ItemCount));
    }
    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson)
    {
        ParseCalls++;
        if (capability != new AssistantCapabilityReference("fixture.proposal", 1)) return AiAssistantErrors.InvalidOutput;
        try
        {
            using var json = JsonDocument.Parse(outputJson);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return AiAssistantErrors.InvalidOutput;
            var names = root.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != 2 || names.Distinct(StringComparer.Ordinal).Count() != 2 || names.Except(["resourceId", "privateText"], StringComparer.Ordinal).Any())
                return AiAssistantErrors.InvalidOutput;
            if (!root.GetProperty("resourceId").TryGetGuid(out var id) || id == Guid.Empty || root.GetProperty("privateText").ValueKind != JsonValueKind.String)
                return AiAssistantErrors.InvalidOutput;
            return new RoundtripAssistantPayload(id, root.GetProperty("privateText").GetString()!);
        }
        catch (JsonException) { return AiAssistantErrors.InvalidOutput; }
        catch (InvalidOperationException) { return AiAssistantErrors.InvalidOutput; }
    }
}
internal sealed record RoundtripAssistantPayload(Guid ResourceId, string PrivateText) : AssistantCapabilityPayload;
