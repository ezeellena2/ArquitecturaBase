using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiConversationRow(Guid Id, Guid OwnerUserId, AiConversationState State, long Revision,
    DateTime LastActivityAtUtc, string[] RequiredPermissions, Guid? ActiveRequestId, AiConversationQuestionRow? FirstQuestion = null)
{
    public override string ToString() => "AiConversationRow { content: [REDACTED] }";
}
public sealed record AiConversationQuestionRow(Guid TurnId, long PayloadRevision, string? ProtectedQuestion, DateTime ContentExpiresAtUtc)
{
    public override string ToString() => "AiConversationQuestionRow { content: [REDACTED] }";
}
public sealed record AiAssistantTurnRow(Guid Id, Guid OwnerUserId, Guid ConversationId, Guid RequestId, int Ordinal,
    AiAssistantTurnState State, string Stage, long Revision, long ConversationRevision, string[] RequiredPermissions,
    string? ProtectedQuestion, string? ProtectedAnswer, string? ProtectedEvidences, long PayloadRevision,
    string Culture, string TimeZoneId, AiProvider Provider, string Model, long ConnectionRevision,
    Guid? AttemptId, long Fence, DateTime SubmittedAtUtc, DateTime ContentExpiresAtUtc, DateTime? DeadlineAtUtc,
    DateTime? CancelRequestedAtUtc, int ProviderSteps, int ToolCalls, int ToolBytes,
    long? InputTokens, long? OutputTokens, bool UsageUnknown, string? FailureCode)
{
    public override string ToString() => "AiAssistantTurnRow { content: [REDACTED] }";
}
public sealed record AiAssistantBudgetCounts(int ActorAccepted, int InstallationAccepted, int Pending, int Running);
public sealed record AiAssistantWorkloadRow(int Pending, int Running, int ExpiredRunning,
    DateTime? OldestPendingAtUtc, DateTime? OldestOverdueContentAtUtc, int AcceptedToday = 0);
