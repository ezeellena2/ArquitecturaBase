using System.Text.Json.Serialization;
using ArquitecturaBase.Application.Models.Assistant;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiAssistantSuggestion(string Id, string Question);
public sealed record AiAssistantPublicLimits(int QuestionMaxLength);
public sealed record AiAssistantBootstrapResponse(string Availability, long ConnectionRevision,
    IReadOnlyList<AiAssistantSuggestion> Suggestions, AiAssistantPublicLimits Limits, string? ConfigurationDestination);
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
public sealed record AiAssistantTurnResponse(Guid TurnId, Guid ConversationId, Guid RequestId, int Ordinal,
    long ConversationRevision, long TurnRevision, string State, string Stage, string ContentAccess,
    string? Question, AiAssistantAnswer? Answer,
    IReadOnlyList<ArquitecturaBase.Application.Models.SystemQueries.SystemQueryEvidence> Evidences,
    AiAssistantFailure? Failure, LlmConversationUsage? Usage, string Culture, string TimeZoneId,
    DateTime SubmittedAtUtc, DateTime ServerNowUtc, int PollingAfterSeconds)
{
    public DateTime? CancelRequestedAtUtc { get; init; }
    [JsonIgnore]
    public AssistantObservedProposal? Proposal { get; init; }
    public override string ToString() => "AiAssistantTurnResponse { content: [REDACTED] }";
}
