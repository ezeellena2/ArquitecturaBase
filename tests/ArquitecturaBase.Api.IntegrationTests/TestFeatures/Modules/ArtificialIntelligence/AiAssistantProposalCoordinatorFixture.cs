using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Api.IntegrationTests.TestFeatures.Modules.ArtificialIntelligence;

internal sealed class AiAssistantProposalCoordinatorFixture : IAiAssistantDispatchService, IAiAssistantExecutionService, IAiAssistantReader
{
    private readonly AiAssistantClaimResponse claim;
    private readonly AiAssistantExecutionContextResponse context;
    private readonly Action? stopAfterAdvance;
    private bool claimed;
    public AssistantObservedProposal Proposal { get; }
    public AssistantObservedProposal? CompletedProposal { get; private set; }
    public int ProviderCalls { get; private set; }
    public int BeginCalls { get; private set; }
    public int CompleteCalls { get; private set; }
    public int RecordCalls { get; private set; }
    public int FailCalls { get; private set; }
    public bool CompletionTokenWasCanceled { get; private set; }
    public AiAssistantProposalCoordinatorFixture(TimeProvider clock, Action? stopAfterAdvance)
    {
        this.stopAfterAdvance = stopAfterAdvance;
        var now = clock.GetUtcNow().UtcDateTime;
        var actor = Guid.NewGuid();
        var conversation = AiConversation.Create(actor, now, "fixture-proposal");
        var turn = AiAssistantTurn.Create(actor, conversation.Id, Guid.NewGuid(), 1, "protected-question", "en", "UTC", AiProvider.OpenAI,
            "fixture-model", 1, now, now.AddDays(30));
        turn.Claim(Guid.NewGuid(), now.AddSeconds(90));
        claim = new(turn.Id, actor, conversation.Id, turn.RequestId, turn.Fence, turn.AttemptId!.Value, turn.DeadlineAtUtc!.Value, "en");
        var row = new AiAssistantTurnRow(turn.Id, actor, conversation.Id, turn.RequestId, 1, turn.State, turn.Stage, turn.Revision, 1, [],
            turn.ProtectedQuestion, null, null, 1, "en", "UTC", AiProvider.OpenAI, "fixture-model", 1, turn.AttemptId, turn.Fence,
            now, now.AddDays(30), turn.DeadlineAtUtc, null, 0, 0, 0, null, null, false, null);
        Proposal = new(turn.Id, Guid.NewGuid(), new("fixture.proposal", 1), new CoordinatorFixturePayload("fictitious"));
        context = new(claim, row, new(AiProvider.OpenAI, "fixture-model", "fictitious-key", "fixture policy", [], [], [], null, 1500, 262144, 131072), [], [], 0, 0)
        { Proposals = [Proposal] };
    }
    public Task<Result<AiAssistantClaimResponse?>> ClaimAsync(CancellationToken cancellationToken)
    {
        if (claimed) return Task.FromResult(Result.Success<AiAssistantClaimResponse?>(null));
        claimed = true;
        return Task.FromResult(Result.Success<AiAssistantClaimResponse?>(claim));
    }
    public Task<Result<bool>> BeginProviderStepAsync(AiAssistantClaimResponse accepted, CancellationToken cancellationToken)
    {
        BeginCalls++;
        return Task.FromResult(Result.Success(BeginCalls == 1));
    }
    public Task<Result<AiAssistantExecutionContextResponse>> PrepareAsync(AiAssistantClaimResponse accepted, CancellationToken cancellationToken) => Task.FromResult(Result.Success(context));
    public Task<Result<LlmConversationResponse>> CallProviderAsync(AiAssistantExecutionContextResponse accepted, CancellationToken cancellationToken)
    {
        ProviderCalls++;
        return Task.FromResult<Result<LlmConversationResponse>>(new LlmConversationResponse("tools", [], null, new(10, 2)));
    }
    public Task<Result<AiAssistantAdvanceResponse>> AdvanceAsync(AiAssistantExecutionContextResponse accepted, LlmConversationResponse response, CancellationToken cancellationToken)
    {
        stopAfterAdvance?.Invoke();
        return Task.FromResult(Result.Success(new AiAssistantAdvanceResponse(context, null, [], response.Usage) { Proposal = Proposal }));
    }
    public Task<Result> CompleteAsync(AiAssistantClaimResponse accepted, AiAssistantAdvanceResponse completion, CancellationToken cancellationToken)
    {
        CompleteCalls++;
        CompletionTokenWasCanceled = cancellationToken.IsCancellationRequested;
        CompletedProposal = completion.Proposal;
        return Task.FromResult(Result.Success());
    }
    public Task<Result> RecordProgressAsync(AiAssistantClaimResponse accepted, AiAssistantAdvanceResponse progress, CancellationToken cancellationToken)
    {
        RecordCalls++;
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result.Success());
    }
    public Task<Result> FailAsync(AiAssistantClaimResponse accepted, string failureCode, bool uncertain, CancellationToken cancellationToken, LlmConversationUsage? usage = null)
    {
        FailCalls++;
        return Task.FromResult(Result.Success());
    }
    private AiAssistantTurnRow Row => CompleteCalls > 0 ? context.Turn with { State = AiAssistantTurnState.Completed } : context.Turn;
    public Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken) => Task.FromResult<AiAssistantTurnRow?>(Row);
    public Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid actor, Guid requestId, CancellationToken cancellationToken) => Task.FromResult<AiAssistantTurnRow?>(Row);
    public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid actor, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<AiConversationRow?> FindConversationAsync(Guid actor, Guid conversationId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid actor, ListAiConversationsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
}
internal sealed record CoordinatorFixturePayload(string PrivateText) : AssistantCapabilityPayload;
