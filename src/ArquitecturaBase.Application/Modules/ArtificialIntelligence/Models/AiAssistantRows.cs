using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiConversationRow(Guid Id, Guid OwnerUserId, AiConversationState State, long Revision,
    DateTime LastActivityAtUtc, string[] RequiredPermissions, Guid? ActiveRequestId, AiConversationQuestionRow? FirstQuestion = null)
{
    public override string ToString() => "AiConversationRow { content: [REDACTED] }";
}
public sealed record AiConversationQuestionRow(Guid TurnId, long PayloadRevision, string? ProtectedQuestion, DateTime ContentExpiresAtUtc)
{
    public string? ProtectedTranscript { get; init; }
    public string? AttachmentKey { get; init; }
    /// <summary>Only for a first turn without text, to name the conversation after its spreadsheet: the page never brings it.</summary>
    public string? ProtectedAttachment { get; init; }
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
    public decimal? AudioSeconds { get; init; }
    public Guid? AudioRequestId { get; init; }
    public string? ProtectedTranscript { get; init; }
    public string? AttachmentKey { get; init; }
    public string? AttachmentSha256 { get; init; }
    public long? AttachmentSize { get; init; }
    /// <summary>Never projected: who shows or names a turn's spreadsheet asks for it (AiAssistantAttachmentPolicy).</summary>
    public string? ProtectedAttachment { get; init; }
    public string ChannelId { get; init; } = "home";
    public string? ExecutionProfileJson { get; init; }
    public IReadOnlyList<AiAssistantToolResultRow> ToolResults { get; init; } = [];
    public override string ToString() => "AiAssistantTurnRow { content: [REDACTED] }";
}
/// <summary>The spreadsheet a turn has, own or inherited, for the next turn of its conversation to inherit it.</summary>
public sealed record AiAssistantAttachmentRow(Guid TurnId, long PayloadRevision, DateTime ContentExpiresAtUtc, string AttachmentKey, string? ProtectedAttachment)
{
    public override string ToString() => "AiAssistantAttachmentRow { content: [REDACTED] }";
}
/// <summary>What a turn named before it was accepted, for removing its conversation (I2): its recording and, for a turn with a file, the
/// profile that seals which reading it named.</summary>
public sealed record AiAssistantNamedSourceRow(Guid? AudioRequestId, string? ExecutionProfileJson);
/// <summary>How the purge tells a Home recording or reading no turn named (I2): a turn names a recording by its request and holds the copy
/// of a reading, whose key names the reading's request.</summary>
public sealed record AiAssistantUnnamedSources(DateTime SubmittedBeforeUtc, string AudioChannelId, string DocumentChannelId, string DocumentKeyPrefix);
public sealed record AiAssistantBudgetCounts(int ActorAccepted, int InstallationAccepted, int Pending, int Running);
public sealed record AiAssistantWorkloadRow(int Pending, int Running, int ExpiredRunning,
    DateTime? OldestPendingAtUtc, DateTime? OldestOverdueContentAtUtc, int AcceptedToday = 0);

public sealed record AiAssistantToolResultRow(int Ordinal, string ToolId, int Version, string? ProtectedResult)
{
    public override string ToString() => "AiAssistantToolResultRow { content: [REDACTED] }";
}
