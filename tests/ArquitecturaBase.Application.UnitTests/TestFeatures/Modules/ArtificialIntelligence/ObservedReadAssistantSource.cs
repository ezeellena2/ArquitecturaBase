using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Assistant;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.UnitTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class ObservedReadAssistantSource : IAssistantCapabilitySource, IAssistantCapabilityExecutor
{
    public const string ReadId = "fixture.observed-resource";
    public const string ProposalId = "fixture.observed-proposal";
    public Guid ResourceId { get; } = Guid.Parse("40000000-0000-0000-0000-000000000081");
    public string CurrentName { get; set; } = "fictional-private-observed-name";
    public int CurrentRevision { get; set; } = 7;
    public int ReadExecutions { get; private set; }
    public int ProposalExecutions { get; private set; }
    public int ReadParseCalls { get; private set; }
    public int ProposalParseCalls { get; private set; }
    public AssistantCapabilityRequest? LastProposalRequest { get; private set; }
    public IReadOnlyList<AssistantCapabilityDefinition> Definitions { get; } =
    [
        new(ReadId, 1, AssistantCapabilityKind.Read, "Read fictional resource metadata",
            """{"type":"object","properties":{},"required":[],"additionalProperties":false}""",
            OutputSchema, new([], []), new(4096, 16384, 1, 5)),
        new(ProposalId, 1, AssistantCapabilityKind.Proposal, "Propose only using earlier observed metadata",
            """{"type":"object","properties":{"resourceId":{"type":"string"}},"required":["resourceId"],"additionalProperties":false}""",
            OutputSchema, new([], []), new(4096, 16384, 1, 5))
    ];
    private const string OutputSchema = """{"type":"object","properties":{"resourceId":{"type":"string"},"revision":{"type":"integer"},"name":{"type":"string"}},"required":["resourceId","revision","name"],"additionalProperties":false}""";

    public Task<Result<AssistantCapabilityResponse>> ExecuteAsync(AssistantCapabilityRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Capability == new AssistantCapabilityReference(ReadId, 1))
        {
            ReadExecutions++;
            return Task.FromResult<Result<AssistantCapabilityResponse>>(new AssistantCapabilityResponse("complete",
                new ObservedResourceReadPayload(ResourceId, CurrentRevision, CurrentName), [], 1));
        }
        ProposalExecutions++;
        LastProposalRequest = request;
        if (request.Capability != new AssistantCapabilityReference(ProposalId, 1)) return Rejected();
        using var arguments = JsonDocument.Parse(request.ArgumentsJson);
        var root = arguments.RootElement;
        if (!Closed(root, "resourceId") || !root.GetProperty("resourceId").TryGetGuid(out var resourceId)) return Rejected();
        var observed = request.ObservedReads.SingleOrDefault(read => read.TurnId == request.TurnId && read.Ordinal > 0
            && read.Capability == new AssistantCapabilityReference(ReadId, 1)
            && read.Payload is ObservedResourceReadPayload payload && payload.ResourceId == resourceId);
        if (observed?.Payload is not ObservedResourceReadPayload snapshot) return Rejected();
        return Task.FromResult<Result<AssistantCapabilityResponse>>(new AssistantCapabilityResponse("proposed",
            new ObservedResourceProposalPayload(snapshot.ResourceId, snapshot.Revision, snapshot.Name), [], 1));
    }

    public Result<AssistantCapabilityPayload> ParseOutput(AssistantCapabilityReference capability, string outputJson)
    {
        if (capability.Version != 1 || capability.Id is not (ReadId or ProposalId)) return AiAssistantErrors.InvalidOutput;
        if (capability.Id == ReadId) ReadParseCalls++; else ProposalParseCalls++;
        try
        {
            using var json = JsonDocument.Parse(outputJson);
            var root = json.RootElement;
            if (!Closed(root, "resourceId", "revision", "name") || !root.GetProperty("resourceId").TryGetGuid(out var resourceId)
                || resourceId == Guid.Empty || !root.GetProperty("revision").TryGetInt32(out var revision) || revision <= 0
                || root.GetProperty("name").ValueKind != JsonValueKind.String) return AiAssistantErrors.InvalidOutput;
            AssistantCapabilityPayload payload = capability.Id == ReadId
                ? new ObservedResourceReadPayload(resourceId, revision, root.GetProperty("name").GetString()!)
                : new ObservedResourceProposalPayload(resourceId, revision, root.GetProperty("name").GetString()!);
            return payload;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return AiAssistantErrors.InvalidOutput; }
    }

    private static Task<Result<AssistantCapabilityResponse>> Rejected() =>
        Task.FromResult<Result<AssistantCapabilityResponse>>(AiAssistantErrors.ToolRejected);
    private static bool Closed(JsonElement value, params string[] properties)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var names = value.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == properties.Length && names.Distinct(StringComparer.Ordinal).Count() == names.Length
            && names.All(name => properties.Contains(name, StringComparer.Ordinal));
    }
}
internal sealed record ObservedResourceReadPayload(Guid ResourceId, int Revision, string Name) : AssistantCapabilityPayload;
internal sealed record ObservedResourceProposalPayload(Guid ResourceId, int Revision, string Name) : AssistantCapabilityPayload;
