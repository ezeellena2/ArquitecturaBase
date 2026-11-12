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
    /// <summary>Stage 3: whether this person may attach a Word (private storage, and the owner's port allows documents).</summary>
    public bool Documents { get; init; }
    /// <summary>Stage 3: whether this person may also attach a photo or a PDF: documents, and this installation reads them.</summary>
    public bool DocumentReading { get; init; }
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
    // Only the turn that brought a file (a spreadsheet, Stage 2, or a document, Stage 3) shows it; the turns that inherit it keep their shape.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AiAssistantTurnAttachmentResponse? Attachment { get; init; }
    [JsonIgnore]
    public AssistantObservedProposal? ObservedProposal { get; init; }

    /// <summary>Every proposal the turn closed on, in order; the first one is <see cref="ObservedProposal"/>.</summary>
    [JsonIgnore]
    public IReadOnlyList<AssistantObservedProposal> ObservedProposals { get; init; } = [];
    [JsonIgnore]
    public AssistantNormalizedAudio? NormalizedAudio { get; init; }
    [JsonIgnore]
    public ArquitecturaBase.Application.Models.StructuredData.StructuredDocumentReading? DocumentReading { get; init; }
    // The document a completed reading read (the descriptor of its copy), with DocumentReading.
    [JsonIgnore]
    public AssistantDocumentInput? ReadDocument { get; init; }
    // The name of the document the turn brought, for the history to name it rather than a spreadsheet.
    [JsonIgnore]
    public string? AttachedDocument { get; init; }
    public override string ToString() => "AiAssistantTurnResponse { content: [REDACTED] }";
}

public sealed record AiAssistantTurnAudioResponse(Guid RequestId, string Transcript)
{ public override string ToString() => "AiAssistantTurnAudioResponse { Transcript = [REDACTED] }"; }

/// <summary>
/// The file a Home turn brought, for its bubble: the file's name, its kind (xlsx or csv for a spreadsheet; pdf, jpeg, png or docx for a
/// document) and its size; a spreadsheet counts its rows with content, and a document its pages (a photo is one; a Word has none).
/// </summary>
public sealed record AiAssistantTurnAttachmentResponse(string Name, string Kind,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Rows, long Size)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Pages { get; init; }
    public override string ToString() => "AiAssistantTurnAttachmentResponse { Name = [REDACTED] }";
}
