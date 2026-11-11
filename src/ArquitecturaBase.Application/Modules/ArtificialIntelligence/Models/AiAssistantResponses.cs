using System.Text.Json;
using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiAssistantSuggestion(string Id, string Question);
public sealed record AiAssistantPublicLimits(int QuestionMaxLength);
public sealed record AiAssistantBootstrapResponse(string Availability, long ConnectionRevision,
    IReadOnlyList<AiAssistantSuggestion> Suggestions, AiAssistantPublicLimits Limits, string? ConfigurationDestination)
{
    /// <summary>IA4: whether Home takes a recording (a provider that hears what the browser records and private storage).</summary>
    public bool Audio { get; init; }
    /// <summary>Stage 2: whether this person may attach a spreadsheet (private storage, and the owner's port allows them).</summary>
    public bool Attachments { get; init; }
}
public sealed record AiConversationSummaryResponse(Guid ConversationId, long Revision, string Title,
    DateTime LastActivityAtUtc, string ContentAccess)
{
    public override string ToString() => "AiConversationSummaryResponse { content: [REDACTED] }";
}
public sealed record AiConversationResponse(Guid ConversationId, long Revision, string Title,
    string ContentAccess, Guid? ActiveRequestId, DateTime LastActivityAtUtc)
{
    public override string ToString() => "AiConversationResponse { content: [REDACTED] }";
}
public sealed record AiAssistantTurnAcceptedResponse(Guid ConversationId, Guid RequestId, long ConversationRevision,
    long TurnRevision, string State, string Stage, DateTime SubmittedAtUtc, DateTime ServerNowUtc, int PollingAfterSeconds);
public sealed record AiAssistantAnswer(string Kind, string Text, IReadOnlyList<Guid> EvidenceIds)
{
    public override string ToString() => "AiAssistantAnswer { content: [REDACTED] }";
}
public sealed record AiAssistantFailure(string Code, string Detail, string Recovery);

/// <summary>The proposal a Home turn closed on: capability and owner content, never observed reads, prompts or continuation.</summary>
public sealed record AiAssistantProposalResponse(Guid Id, string Capability, int Version, JsonElement Content)
{
    public override string ToString() => "AiAssistantProposalResponse { content: [REDACTED] }";
}
public sealed record AiAssistantTurnResponse(Guid TurnId, Guid ConversationId, Guid RequestId, int Ordinal,
    long ConversationRevision, long TurnRevision, string State, string Stage, string ContentAccess,
    string? Question, AiAssistantAnswer? Answer,
    IReadOnlyList<ArquitecturaBase.Application.Models.SystemQueries.SystemQueryEvidence> Evidences,
    AiAssistantFailure? Failure, LlmConversationUsage? Usage, string Culture, string TimeZoneId,
    DateTime SubmittedAtUtc, DateTime ServerNowUtc, int PollingAfterSeconds)
{
    public DateTime? CancelRequestedAtUtc { get; init; }
    // Only a turn that closed on a proposal carries it: the others keep the shape the front already reads.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiAssistantProposalResponse? Proposal { get; init; }
    // Only a dictated turn (IA4) carries its audio and the transcript the model read; a typed one keeps its shape.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiAssistantTurnAudioResponse? Audio { get; init; }
    // Only the turn that brought a spreadsheet (Stage 2) shows it; the turns that inherit it keep their shape.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiAssistantTurnAttachmentResponse? Attachment { get; init; }
    [JsonIgnore]
    public AssistantObservedProposal? ObservedProposal { get; init; }
    [JsonIgnore]
    public AssistantNormalizedAudio? NormalizedAudio { get; init; }
    public override string ToString() => "AiAssistantTurnResponse { content: [REDACTED] }";
}

public sealed record AiAssistantTurnAudioResponse(Guid RequestId, string Transcript)
{ public override string ToString() => "AiAssistantTurnAudioResponse { Transcript = [REDACTED] }"; }

/// <summary>The spreadsheet a Home turn brought, for its bubble: the file's name, its kind (xlsx or csv), its rows with content and its size.</summary>
public sealed record AiAssistantTurnAttachmentResponse(string Name, string Kind, int Rows, long Size)
{ public override string ToString() => "AiAssistantTurnAttachmentResponse { Name = [REDACTED] }"; }
