using ArquitecturaBase.Application.Models.Assistant;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>Sealed at acceptance, never rebuilt from the worker's current configuration.</summary>
public sealed record AiAssistantExecutionProfileSnapshot(
    string ModelProfileId, int ModelProfileVersion, string InstructionsId, int InstructionsVersion,
    AssistantChannelContext ChannelContext)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AiAssistantPurpose Purpose { get; init; }
    public DateTime? OccurredAtUtc { get; init; }
}

public enum AiAssistantPurpose { Interpret, NormalizeAudio }

public sealed record AiAssistantCapabilityCatalogResponse(
    IReadOnlyList<AssistantCapabilityDefinition> Capabilities, IReadOnlyList<AssistantTerminalReference> Terminals,
    AssistantPermissionRequirements RequiredPermissions);
