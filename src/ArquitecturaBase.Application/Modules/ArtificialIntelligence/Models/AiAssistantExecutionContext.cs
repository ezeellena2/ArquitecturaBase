using ArquitecturaBase.Application.Models.SystemQueries;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiAssistantClaimResponse(Guid TurnId, Guid OwnerUserId, Guid ConversationId, Guid RequestId,
    long Fence, Guid AttemptId, DateTime DeadlineAtUtc, string Culture);
public sealed record AiAssistantExecutionContextResponse(AiAssistantClaimResponse Claim, AiAssistantTurnRow Turn,
    LlmConversationRequest ProviderRequest, IReadOnlyList<SystemQueryEvidence> Evidences,
    IReadOnlyList<AiAssistantObservedTool> ObservedTools, int ToolCalls, int ToolBytes)
{
    public override string ToString() => "AiAssistantExecutionContextResponse { content: [REDACTED] }";
}
public sealed record AiAssistantObservedTool(int Ordinal, string CallId, string ToolId, int Version,
    IReadOnlyList<string> RequiredPermissions, SystemQueryEvidence? Evidence, string ResultJson, int Bytes)
{
    public string State { get; init; } = "complete";
    public DateTime ObservedAtUtc { get; init; }
    public override string ToString() => "AiAssistantObservedTool { content: [REDACTED] }";
}
public sealed record AiAssistantAdvanceResponse(AiAssistantExecutionContextResponse Context, AiAssistantAnswer? Answer,
    IReadOnlyList<AiAssistantObservedTool> NewTools, LlmConversationUsage? Usage);
