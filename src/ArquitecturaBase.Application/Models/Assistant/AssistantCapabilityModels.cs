using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.SystemQueries;
using System.Text.Json.Serialization;

namespace ArquitecturaBase.Application.Models.Assistant;

public enum AssistantCapabilityKind { Read, Proposal }
public enum AssistantInteractionMode { Unspecified = 0, Assistant = 1, Manual = 2 }

/// <summary>
/// Which file of the turn its owner needs to offer a capability: any or none (the default), only a spreadsheet, only a read document, or
/// anything but a document.
/// </summary>
public enum AssistantCapabilityAttachment { Any, Spreadsheet, Document, NoDocument }
public sealed record AssistantCapabilityReference(string Id, int Version);
public sealed record AssistantTerminalReference(string Id, int Version);
public sealed record AssistantCapabilityBudget(int InputBytes, int OutputBytes, int MaximumItems, int TimeoutSeconds);

/// <summary>Requirements apply to each explicit identity separately, without inheritance.</summary>
public sealed record AssistantPermissionRequirements(IReadOnlyList<string> Actor, IReadOnlyList<string> Subject);

public sealed record AssistantCapabilityDefinition(
    string Id, int Version, AssistantCapabilityKind Kind, string Description,
    string InputSchemaJson, string OutputSchemaJson, AssistantPermissionRequirements RequiredPermissions,
    AssistantCapabilityBudget Budget)
{
    /// <summary>The owner allows Home, the actor's own conversation without a Subject, to offer it; Home offers nothing else beyond its core queries.</summary>
    public bool OfferedInHome { get; init; }

    /// <summary>The file the owner needs: a turn whose file it does not admit does not offer it, in Home or in a channel.</summary>
    public AssistantCapabilityAttachment Attachment { get; init; }

    /// <summary>Whether a turn that carries <paramref name="attachment"/> may offer it: the one rule for the catalog and its consumers.</summary>
    public bool OfferedWith(AssistantAttachmentKind attachment) => Attachment switch
    {
        AssistantCapabilityAttachment.Any => true,
        AssistantCapabilityAttachment.Spreadsheet => attachment == AssistantAttachmentKind.Spreadsheet,
        AssistantCapabilityAttachment.Document => attachment == AssistantAttachmentKind.Document,
        AssistantCapabilityAttachment.NoDocument => attachment != AssistantAttachmentKind.Document,
        _ => false,
    };

    /// <summary>The proposal carries text the model wrote for people: offered only with a model profile qualified to draft (ADR 0021).</summary>
    public bool DraftsText { get; init; }
}

/// <summary>Trusted server context; it is never an HTTP or model-supplied contract.</summary>
public sealed record AssistantChannelContext(
    string ChannelId, Guid ActorUserId, Guid? SubjectUserId, string Culture, string TimeZoneId,
    AssistantPermissionRequirements RequiredPermissions,
    IReadOnlyList<AssistantCapabilityReference> AllowedCapabilities,
    IReadOnlyList<AssistantTerminalReference> AllowedTerminals)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public AssistantInteractionMode InteractionMode { get; init; }

    /// <summary>Opaque conversation identity supplied by its channel owner; unrelated to the engine conversation ID.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ChannelConversationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceRecordId { get; init; }

    /// <summary>Reads the engine runs before the first step, in this order, and sends at the start of the request as data; never tools.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AssistantCapabilityReference>? PreloadedCapabilities { get; init; }

    public override string ToString() => "AssistantChannelContext { Identity = [REDACTED] }";
}

public sealed record AssistantCapabilityRequest(
    Guid TurnId, AssistantChannelContext Context, AssistantCapabilityReference Capability, string ArgumentsJson,
    IReadOnlyList<AssistantObservedRead> ObservedReads)
{
    /// <summary>The turn's prepared spreadsheet, or null: the owner reads it here and never opens the private copy.</summary>
    public StructuredSpreadsheet? Attachment { get; init; }
    /// <summary>The turn's read document, or null: apart from <see cref="Attachment"/>, so the owners of spreadsheets do not change.</summary>
    public StructuredDocument? Document { get; init; }
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
