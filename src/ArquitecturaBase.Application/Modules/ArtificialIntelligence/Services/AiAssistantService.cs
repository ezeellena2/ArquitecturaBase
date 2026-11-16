using ArquitecturaBase.Application.Common.Logging;
using Microsoft.Extensions.Logging;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Services;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantService(IAiAssistantRepository repository, IUnitOfWork unitOfWork, IRequestValidator validator, AiAssistantAccessGuard guard, AiAssistantReadPolicy reads, AiAssistantSubmissionPolicy submissions, TimeProvider timeProvider, ILogger<AiAssistantService> logger) : IAiAssistantService
{
    public Task<Result<AiAssistantBootstrapResponse>> BootstrapAsync(CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantBootstrapResponse>>(logger, "BootstrapAssistant", () => reads.BootstrapAsync(cancellationToken));
    public Task<Result<PagedResult<AiConversationSummaryResponse>>> ListConversationsAsync(ListAiConversationsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<AiConversationSummaryResponse>>>(logger, "ListAssistantConversations", () => reads.ListConversationsAsync(request, cancellationToken));
    public Task<Result<AiConversationResponse>> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiConversationResponse>>(logger, "GetAssistantConversation", () => reads.GetConversationAsync(conversationId, cancellationToken));
    public Task<Result<PagedResult<AiAssistantTurnResponse>>> ListTurnsAsync(Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<PagedResult<AiAssistantTurnResponse>>>(logger, "ListAssistantTurns", () => reads.ListTurnsAsync(conversationId, request, cancellationToken));
    public Task<Result<AiAssistantTurnResponse>> GetTurnAsync(Guid requestId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantTurnResponse>>(logger, "GetAssistantTurn", () => reads.GetTurnAsync(requestId, cancellationToken));
    public Task<Result<AiAssistantTurnAcceptedResponse>> SubmitAsync(SubmitAiAssistantTurnRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantTurnAcceptedResponse>>(logger, "SubmitAsync", async () =>
        {
            ArgumentNullException.ThrowIfNull(request);
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            return await unitOfWork.ExecuteInTransactionAsync(ct => submissions.AcceptAsync(userId, request, ct), CommitPolicy.OnSuccess, cancellationToken);
        });

    public Task<Result<AiAssistantTurnResponse>> CancelAsync(CancelAiAssistantTurnRequest request, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result<AiAssistantTurnResponse>>(logger, "CancelAsync", async () =>
        {
            if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            var result = await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await repository.LockBudgetAsync(ct);
                await repository.LockActorAsync(userId, ct);
                var actor = await guard.AuthorizeAsync(userId, ct);
                if (actor.IsFailure) return Result.Failure(actor.Error);
                var receipt = await repository.GetReceiptAsync(userId, request.RequestId, ct);
                if (receipt is null) return Result.Failure(AiAssistantErrors.NotFound);
                await repository.LockConversationAsync(receipt.ConversationId, ct);
                var conversation = await repository.GetConversationAsync(userId, receipt.ConversationId, ct);
                var turn = await repository.GetTurnAsync(receipt.TurnId, ct);
                if (conversation is null || conversation.ChannelId != "home" || turn is null) return Result.Failure(AiAssistantErrors.NotFound);
                turn.RequestCancellation(timeProvider.GetUtcNow().UtcDateTime);
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
            if (result.IsFailure) return result.Error;
            return await GetTurnAsync(request.RequestId, cancellationToken);
        });

    public Task<Result> RemoveConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
        OperationLog.RunAsync<Result>(logger, "RemoveConversationAsync", async () =>
        {
            if (conversationId == Guid.Empty) return AiAssistantErrors.Invalid;
            if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
            return await unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                await repository.LockBudgetAsync(ct);
                await repository.LockActorAsync(userId, ct);
                var actor = await guard.AuthorizeAsync(userId, ct);
                if (actor.IsFailure) return Result.Failure(actor.Error);
                // I2: the readings and normalizations its turns named go with it. Under the budget lock no turn is added or claimed, so
                // they are read first and every conversation is locked in the house order, sorted, as the purge does.
                var sources = await NamedSourcesAsync(userId, conversationId, ct);
                foreach (var locked in sources.Select(source => source.ConversationId).Append(conversationId).Distinct().Order())
                    await repository.LockConversationAsync(locked, ct);
                var conversation = await repository.GetConversationAsync(userId, conversationId, ct);
                if (conversation is null || conversation.ChannelId != "home") return Result.Failure(AiAssistantErrors.NotFound);
                var now = timeProvider.GetUtcNow().UtcDateTime;
                var active = await repository.GetActiveForConversationAsync(conversationId, ct);
                active?.RequestCancellation(now);
                conversation.Remove(now);
                // Another conversation that took the same reading or transcript keeps the copy sealed in its turn.
                foreach (var source in sources)
                    if ((await repository.GetConversationAsync(userId, source.ConversationId, ct))?.ChannelId == source.ChannelId
                        && await repository.GetTurnAsync(source.TurnId, ct) is { ProtectedQuestion: not null } named && named.OwnerUserId == userId)
                        named.PurgeContent();
                return Result.Success();
            }, CommitPolicy.OnSuccess, cancellationToken);
        });

    // What the turns of the conversation named, by its receipt: a recording by its request and a reading by the key of the copy a turn holds,
    // which names the reading's request. The sealed profile says the same, but a profile this deploy cannot read would hide it.
    private async Task<IReadOnlyList<(Guid ConversationId, Guid TurnId, string ChannelId)>> NamedSourcesAsync(Guid userId, Guid conversationId,
        CancellationToken ct)
    {
        var named = new HashSet<(Guid RequestId, string ChannelId)>();
        foreach (var row in await repository.ListNamedSourcesAsync(userId, conversationId, ct))
        {
            if (row.AudioRequestId is { } audio) named.Add((audio, AiAssistantChannelPolicy.HomeAudioChannelId));
            if (AiAssistantHomeDocumentPolicy.TryParseKey(row.AttachmentKey, out var document))
                named.Add((document, AiAssistantChannelPolicy.HomeDocumentChannelId));
        }
        var sources = new List<(Guid, Guid, string)>(named.Count);
        foreach (var (requestId, channelId) in named)
            if (await repository.GetReceiptAsync(userId, requestId, ct) is { } receipt) sources.Add((receipt.ConversationId, receipt.TurnId, channelId));
        return sources;
    }

}
