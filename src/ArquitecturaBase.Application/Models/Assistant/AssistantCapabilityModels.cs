using ArquitecturaBase.Application.Models.SystemQueries;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.Assistant;

public enum AssistantCapabilityKind { Read, Proposal }
public enum AssistantInteractionMode { Unspecified = 0, Assistant = 1, Manual = 2 }
public sealed record AssistantCapabilityReference(string Id, int Version);
public sealed record AssistantTerminalReference(string Id, int Version);
public sealed record AssistantCapabilityBudget(int InputBytes, int OutputBytes, int MaximumItems, int TimeoutSeconds);

/// <summary>Requirements apply to each explicit identity separately, without inheritance.</summary>
public sealed record AssistantPermissionRequirements(IReadOnlyList<string> Actor, IReadOnlyList<string> Subject);

public sealed record AssistantCapabilityDefinition(
    string Id, int Version, AssistantCapabilityKind Kind, string Description,
    string InputSchemaJson, string OutputSchemaJson, AssistantPermissionRequirements RequiredPermissions,
    AssistantCapabilityBudget Budget);

/// <summary>Trusted server context; it is never an HTTP or model-supplied contract.</summary>
public sealed record AssistantChannelContext(
    string ChannelId, Guid ActorUserId, Guid? SubjectUserId, string Culture, string TimeZoneId,
    AssistantPermissionRequirements RequiredPermissions,
    IReadOnlyList<AssistantCapabilityReference> AllowedCapabilities,
    IReadOnlyList<AssistantTerminalReference> AllowedTerminals)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AssistantInteractionMode InteractionMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceRecordId { get; init; }

    public override string ToString() => "AssistantChannelContext { Identity = [REDACTED] }";
}

public sealed record AssistantCapabilityRequest(
    Guid TurnId, AssistantChannelContext Context, AssistantCapabilityReference Capability, string ArgumentsJson,
    IReadOnlyList<AssistantObservedRead> ObservedReads)
{
    public override string ToString() => "AssistantCapabilityRequest { Content = [REDACTED] }";
}

/// <summary>Each owner supplies a concrete typed DTO and its closed input/output codecs.</summary>
public abstract record AssistantCapabilityPayload
{
    public sealed override string ToString() => "AssistantCapabilityPayload { Content = [REDACTED] }";
}

public sealed record AssistantObservedRead(Guid TurnId, int Ordinal, AssistantCapabilityReference Capability, AssistantCapabilityPayload Payload)
{
    public override string ToString() => "AssistantObservedRead { Content = [REDACTED] }";
}

/// <summary>Preserves the existing Home tool-result shape (state/evidence).</summary>
public sealed record AssistantSystemQueryPayload(string State, SystemQueryEvidence? Evidence) : AssistantCapabilityPayload;

public sealed record AssistantCapabilityResponse(
    string State, AssistantCapabilityPayload Payload, IReadOnlyList<SystemQueryEvidence> Evidences, int ItemCount)
{
    public override string ToString() => "AssistantCapabilityResponse { Content = [REDACTED] }";
}

public sealed record AssistantObservedProposal(Guid TurnId, Guid Id, AssistantCapabilityReference Capability, AssistantCapabilityPayload Payload)
{
    public IReadOnlyList<AssistantObservedRead> ObservedReads { get; init; } = [];
    public override string ToString() => "AssistantObservedProposal { Content = [REDACTED] }";
}
