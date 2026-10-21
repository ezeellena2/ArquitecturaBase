using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Models.Assistant;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Integrations;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantDispatchService(IAiAssistantRepository repository, IUnitOfWork unitOfWork, AiAssistantDispatchGuard dispatchGuard, IAiConversationProtector protector, AiAssistantLimits limits, TimeProvider timeProvider, ILogger<AiAssistantDispatchService> logger, IAssistantOperationCompletionSignal completionSignal) : IAiAssistantDispatchService
{
    public Task<Result<AiAssistantClaimResponse?>> ClaimAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantClaimResponse?>>(logger, "ClaimAsync", async () =>
        {
            AiAssistantTurn? terminalTurn = null;
            var outcome = await unitOfWork.ExecuteInTransactionAsync<Result<AiAssistantClaimResponse?>>(async ct =>
            {
                await dispatchGuard.LockInstallationAsync(ct);
                var turn = await repository.GetPendingAsync(ct);
                terminalTurn = turn;
                if (turn is null) return Result.Success<AiAssistantClaimResponse?>(null);
                await repository.LockActorAsync(turn.OwnerUserId, ct);
                await repository.LockConversationAsync(turn.ConversationId, ct);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                turn.Reconcile(now, limits.PendingSeconds, limits.LeaseGraceSeconds);
                if (turn.State != AiAssistantTurnState.Pending) return Result.Success<AiAssistantClaimResponse?>(null);
                var budget = await repository.CountBudgetAsync(turn.OwnerUserId, DateOnly.FromDateTime(now), null, null, ct);
                if (budget.Running >= limits.RunningCapacity) return Result.Success<AiAssistantClaimResponse?>(null);
                var claimed = turn.Claim(Guid.NewGuid(), now.AddSeconds(limits.RunningSeconds));
                if (claimed.IsFailure) return claimed.Error;
                var access = await dispatchGuard.ValidateAsync(turn, ct);
                if (access is not null)
                {
                    turn.Fail(turn.Fence, access.Code, false, now);
                    return Result.Success<AiAssistantClaimResponse?>(null);
                }
                return Result.Success<AiAssistantClaimResponse?>(new(turn.Id, turn.OwnerUserId, turn.ConversationId, turn.RequestId,
                    turn.Fence, turn.AttemptId!.Value, turn.DeadlineAtUtc!.Value, turn.Culture));
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (outcome.IsSuccess) NotifyTerminal(terminalTurn);
            return outcome;
        });

    public Task<Result<bool>> BeginProviderStepAsync(AiAssistantClaimResponse claim, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<bool>>(logger, "BeginProviderStepAsync", async () =>
        {
            AiAssistantTurn? terminalTurn = null;
            var outcome = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await dispatchGuard.LockAsync(claim, ct);
                var turn = await repository.GetTurnAsync(claim.TurnId, ct);
                terminalTurn = turn;
                if (turn is null) return Result.Success(false);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var valid = turn.VerifyFence(claim.Fence, now);
                var access = valid.IsFailure ? valid.Error : await dispatchGuard.ValidateAsync(turn, ct);
                if (access is not null)
                {
                    if (turn.State == AiAssistantTurnState.Running && turn.Fence == claim.Fence) turn.Fail(claim.Fence, access.Code, false, now);
                    return Result.Success(false);
                }
                var begun = turn.BeginProviderStep(claim.Fence, now, limits.ProviderSteps);
                if (begun.IsFailure)
                {
                    turn.Fail(claim.Fence, begun.Error.Code, false, now);
                    return Result.Success(false);
                }
                await dispatchGuard.ReserveSourcesAsync(turn, ct);
                return Result.Success(true);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (outcome.IsSuccess) NotifyTerminal(terminalTurn);
            return outcome;
        });

    public Task<Result> RecordProgressAsync(AiAssistantClaimResponse claim, AiAssistantAdvanceResponse progress, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "RecordProgressAsync", async () =>
        {
            AiAssistantTurn? terminalTurn = null;
            var outcome = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await dispatchGuard.LockAsync(claim, ct);
                var turn = await repository.GetTurnAsync(claim.TurnId, ct);
                terminalTurn = turn;
                if (turn is null) return Result.Failure(AiAssistantErrors.FenceRejected);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var valid = turn.VerifyFence(claim.Fence, now);
                if (valid.IsFailure) return valid;
                var recorded = await RecordToolsAsync(turn, progress, ct);
                if (!recorded) return Result.Success();
                turn.RecordUsage(progress.Usage?.InputTokens, progress.Usage?.OutputTokens, progress.Usage?.AudioSeconds);
                var access = await dispatchGuard.ValidateAsync(turn, ct);
                if (access is not null) return turn.Fail(claim.Fence, access.Code, false, now);
                return turn.RecordProgress(claim.Fence, now, "composing", progress.Context.ToolCalls - turn.ToolCalls,
                    progress.Context.ToolBytes - turn.ToolBytes, null, null);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (outcome.IsSuccess) NotifyTerminal(terminalTurn);
            return outcome;
        });

    public Task<Result> CompleteAsync(AiAssistantClaimResponse claim, AiAssistantAdvanceResponse completion, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "CompleteAsync", async () =>
        {
            AiAssistantTurn? terminalTurn = null;
            var outcome = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await dispatchGuard.LockAsync(claim, ct);
                var turn = await repository.GetTurnAsync(claim.TurnId, ct);
                terminalTurn = turn;
                if (turn is null) return Result.Failure(AiAssistantErrors.FenceRejected);
                if (turn.State == AiAssistantTurnState.Completed && turn.Fence == claim.Fence) return Result.Success();
                var now = timeProvider.GetUtcNow().UtcDateTime;
                if (turn.State != AiAssistantTurnState.Running || turn.Fence != claim.Fence) return Result.Failure(AiAssistantErrors.FenceRejected);
                turn.RecordUsage(completion.Usage?.InputTokens, completion.Usage?.OutputTokens, completion.Usage?.AudioSeconds);
                var valid = turn.VerifyFence(claim.Fence, now);
                var access = valid.IsFailure ? valid.Error : await dispatchGuard.ValidateAsync(turn, ct);
                if (access is not null) return turn.Fail(claim.Fence, access.Code, false, now);
                var execution = AiAssistantChannelPolicy.Read(turn.ExecutionProfileJson);
                var normalization = execution.IsSuccess && execution.Value.Purpose == AiAssistantPurpose.NormalizeAudio;
                var evidence = normalization
                    ? completion.NormalizedAudio is { } normalized && AiAssistantAudioPolicy.Valid(normalized, limits.QuestionCharacters) &&
                        completion.Answer is null && completion.Proposal is null && completion.NewTools.Count == 0 && completion.Context.ToolCalls == 0 &&
                        completion.Context.ObservedTools.Count == 0 && completion.Context.ToolBytes == 0
                        ? Result.Success() : Result.Failure(AiAssistantErrors.InvalidOutput)
                    : completion.NormalizedAudio is not null ? Result.Failure(AiAssistantErrors.InvalidOutput)
                    : completion.Answer is not null ? AiAssistantEvidenceVerifier.Verify(completion.Answer, completion.Context.Evidences)
                    : completion.Proposal is not null && completion.Proposal.TurnId == turn.Id ? Result.Success() : Result.Failure(AiAssistantErrors.InvalidOutput);
                if (evidence.IsFailure) return turn.Fail(claim.Fence, evidence.Error.Code, false, now);
                await RecordToolsAsync(turn, completion, ct);
                turn.RecordProgress(claim.Fence, now, "verifying", completion.Context.ToolCalls - turn.ToolCalls,
                    completion.Context.ToolBytes - turn.ToolBytes, null, null);
                string answerJson;
                if (completion.NormalizedAudio is { } audio)
                    answerJson = JsonSerializer.Serialize(new { kind = "normalizedAudio", audio.Text, audio.Languages }, AiAssistantResponsePolicy.JsonOptions);
                else if (completion.Proposal is { } proposal)
                {
                    var selected = completion.Context.ObservedTools.SingleOrDefault(t => t.ProposalId == proposal.Id &&
                        t.ToolId == proposal.Capability.Id && t.Version == proposal.Capability.Version && t.Kind == AssistantCapabilityKind.Proposal);
                    if (selected is null) return turn.Fail(claim.Fence, AiAssistantErrors.InvalidOutput.Code, false, now);
                    answerJson = JsonSerializer.Serialize(new { kind = "proposal", selected.Ordinal }, AiAssistantResponsePolicy.JsonOptions);
                }
                else answerJson = JsonSerializer.Serialize(completion.Answer, AiAssistantResponsePolicy.JsonOptions);
                var evidenceJson = JsonSerializer.Serialize(completion.Context.Evidences, AiAssistantResponsePolicy.JsonOptions);
                var answer = protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "answer", turn.PayloadRevision, answerJson);
                var evidences = protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "evidences", turn.PayloadRevision, evidenceJson);
                var result = turn.Complete(claim.Fence, answer, evidences, now);
                if (result.IsSuccess)
                {
                    var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
                    conversation!.Touch(now);
                }
                return result;
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (outcome.IsSuccess) NotifyTerminal(terminalTurn);
            return outcome;
        });

    public Task<Result> FailAsync(AiAssistantClaimResponse claim, string failureCode, bool uncertain, CancellationToken cancellationToken, LlmConversationUsage? usage = null) =>
        OperationLog.RunAsync<Result>(logger, "FailAsync", async () =>
        {
            AiAssistantTurn? terminalTurn = null;
            var outcome = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await dispatchGuard.LockAsync(claim, ct);
                var turn = await repository.GetTurnAsync(claim.TurnId, ct);
                terminalTurn = turn;
                if (turn is null) return Result.Failure(AiAssistantErrors.FenceRejected);
                if (turn.State is not (AiAssistantTurnState.Pending or AiAssistantTurnState.Running)) return Result.Success();
                turn.RecordUsage(usage?.InputTokens, usage?.OutputTokens, usage?.AudioSeconds);
                return turn.Fail(claim.Fence, failureCode, uncertain, timeProvider.GetUtcNow().UtcDateTime);
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (outcome.IsSuccess) NotifyTerminal(terminalTurn);
            return outcome;
        });

    private void NotifyTerminal(AiAssistantTurn? turn)
    {
        if (turn is { State: AiAssistantTurnState.Completed or AiAssistantTurnState.Failed or AiAssistantTurnState.Uncertain
            or AiAssistantTurnState.Canceled or AiAssistantTurnState.Expired }) completionSignal.Notify(turn.ChannelId);
    }

    private async Task<bool> RecordToolsAsync(AiAssistantTurn turn, AiAssistantAdvanceResponse progress, CancellationToken ct)
    {
        if (progress.NewTools.Count == 0) return true;
        var previous = await repository.ListToolsAsync(turn.Id, ct);
        var fresh = progress.NewTools.Where(t => !previous.Any(p => p.ToolCallId == t.CallId)).ToArray();
        if (fresh.Length == 0) return false;
        var conversation = await repository.GetConversationAsync(turn.OwnerUserId, turn.ConversationId, ct);
        foreach (var tool in fresh)
        {
            conversation?.RequirePermissions(tool.RequiredPermissions);
            var stored = tool.Kind == AssistantCapabilityKind.Proposal
                ? JsonSerializer.Serialize(new { output = JsonSerializer.Deserialize<JsonElement>(tool.ResultJson), tool.ReadOrdinals }, AiAssistantResponsePolicy.JsonOptions)
                : tool.ResultJson;
            var protectedResult = protector.Protect(turn.OwnerUserId, turn.ConversationId, turn.Id, "tool:" + tool.Ordinal,
                turn.PayloadRevision, stored);
            repository.AddTool(AiAssistantToolExecution.Create(turn.Id, tool.Ordinal, tool.CallId, tool.ToolId, tool.Version,
                tool.RequiredPermissions.ToArray(), tool.Evidence?.Id.ToString("D") ?? string.Empty,
                tool.Evidence?.ObservedAtUtc ?? tool.ObservedAtUtc, protectedResult, tool.Bytes, tool.State));
        }
        return true;
    }
}
