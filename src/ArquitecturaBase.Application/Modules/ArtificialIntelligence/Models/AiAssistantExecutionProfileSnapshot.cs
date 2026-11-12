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
    /// <summary>The file the catalog was decided for, as a number; omitted without one, and in seals from before documents.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AssistantAttachmentKind Attachment { get; init; }
}

/// <summary>Sealed as a number: Interpret is omitted. The technical purposes (audio and documents) have no capabilities, terminals or history.</summary>
public enum AiAssistantPurpose { Interpret = 0, NormalizeAudio = 1, ReadDocument = 2 }

public sealed record AiAssistantCapabilityCatalogResponse(
    IReadOnlyList<AssistantCapabilityDefinition> Capabilities, IReadOnlyList<AssistantTerminalReference> Terminals,
    AssistantPermissionRequirements RequiredPermissions)
{
    /// <summary>The reads the engine runs before the first step, in their order: never tools, outside the sixteen definitions.</summary>
    public IReadOnlyList<AssistantCapabilityDefinition> Preloaded { get; init; } = [];
}
