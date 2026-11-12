using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Models.SystemQueries;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;

public sealed record AiAssistantClaimResponse(Guid TurnId, Guid OwnerUserId, Guid ConversationId, Guid RequestId,
    long Fence, Guid AttemptId, DateTime DeadlineAtUtc, string Culture);
public sealed record AiAssistantExecutionContextResponse(AiAssistantClaimResponse Claim, AiAssistantTurnRow Turn,
    LlmConversationRequest ProviderRequest, IReadOnlyList<SystemQueryEvidence> Evidences,
    IReadOnlyList<AiAssistantObservedTool> ObservedTools, int ToolCalls, int ToolBytes)
{
    public AiAssistantExecutionProfileSnapshot? ExecutionProfile { get; init; }
    public IReadOnlyList<AssistantObservedProposal> Proposals { get; init; } = [];
    public IReadOnlyList<AssistantObservedRead> ObservedReads { get; init; } = [];
    /// <summary>The preloads observed before the first step, recorded with that step's progress or completion.</summary>
    public IReadOnlyList<AiAssistantObservedTool> PreloadedTools { get; init; } = [];
    /// <summary>The turn's prepared spreadsheet, revealed once to build the request; each capability call receives it.</summary>
    public StructuredSpreadsheet? Attachment { get; init; }
    /// <summary>The turn's read document, revealed the same way; each capability call receives it apart from the spreadsheet.</summary>
    public StructuredDocument? Document { get; init; }
    public override string ToString() => "AiAssistantExecutionContextResponse { content: [REDACTED] }";
}
public sealed record AiAssistantObservedTool(int Ordinal, string CallId, string ToolId, int Version,
    IReadOnlyList<string> RequiredPermissions, SystemQueryEvidence? Evidence, string ResultJson, int Bytes)
{
    public string State { get; init; } = "complete";
    public AssistantCapabilityKind Kind { get; init; }
    public Guid? ProposalId { get; init; }
    public IReadOnlyList<int> ReadOrdinals { get; init; } = [];
    public DateTime ObservedAtUtc { get; init; }
    public override string ToString() => "AiAssistantObservedTool { content: [REDACTED] }";
}
public sealed record AiAssistantAdvanceResponse(AiAssistantExecutionContextResponse Context, AiAssistantAnswer? Answer,
    IReadOnlyList<AiAssistantObservedTool> NewTools, LlmConversationUsage? Usage)
{
    /// <summary>The selected proposal, the first one when assistant_proposal@2 selects several.</summary>
    public AssistantObservedProposal? Proposal { get; init; }
    /// <summary>Every proposal selected by assistant_proposal@2, in the model's order; empty with the other terminals.</summary>
    public IReadOnlyList<AssistantObservedProposal> Proposals { get; init; } = [];
    public AssistantNormalizedAudio? NormalizedAudio { get; init; }
    public StructuredDocumentReading? DocumentReading { get; init; }
    public bool IsComplete => Answer is not null || Proposal is not null || NormalizedAudio is not null || DocumentReading is not null;
}
