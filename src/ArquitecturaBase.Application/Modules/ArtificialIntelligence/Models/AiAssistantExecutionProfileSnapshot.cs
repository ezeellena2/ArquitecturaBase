using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

/// <summary>Sealed at acceptance, never rebuilt from the worker's current configuration.</summary>
public sealed record AiAssistantExecutionProfileSnapshot(
    string ModelProfileId, int ModelProfileVersion, string InstructionsId, int InstructionsVersion,
    AssistantChannelContext ChannelContext);

public sealed record AiAssistantCapabilityCatalogResponse(
    IReadOnlyList<AssistantCapabilityDefinition> Capabilities, IReadOnlyList<AssistantTerminalReference> Terminals,
    AssistantPermissionRequirements RequiredPermissions);
