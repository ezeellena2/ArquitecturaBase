using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.ArtificialIntelligence.Persistence.Readers;

internal sealed class AiAssistantReader(ApplicationDbContext dbContext, TimeProvider timeProvider) : IAiAssistantReader
{
    private static readonly Dictionary<string, Expression<Func<AiConversation, object?>>> ConversationSortMap = new()
    {
        ["lastActivityAtUtc"] = conversation => conversation.LastActivityAtUtc,
        ["id"] = conversation => conversation.Id,
    };
    private static readonly Dictionary<string, Expression<Func<AiAssistantTurn, object?>>> TurnSortMap = new()
    {
        ["ordinal"] = turn => turn.Ordinal,
        ["id"] = turn => turn.Id,
    };

    public async Task<AiAssistantWorkloadRow> FindWorkloadAsync(DateTime nowUtc, int leaseGraceSeconds, CancellationToken cancellationToken)
    {
        var turns = dbContext.Set<AiAssistantTurn>().AsNoTracking().IgnoreQueryFilters();
        var cutoffUtc = nowUtc.AddSeconds(-leaseGraceSeconds);
        var acceptedDayUtc = DateOnly.FromDateTime(nowUtc);
        return new AiAssistantWorkloadRow(
            await turns.CountAsync(turn => turn.State == AiAssistantTurnState.Pending, cancellationToken),
            await turns.CountAsync(turn => turn.State == AiAssistantTurnState.Running, cancellationToken),
            await turns.CountAsync(turn => turn.State == AiAssistantTurnState.Running && turn.DeadlineAtUtc <= cutoffUtc, cancellationToken),
            await turns.Where(turn => turn.State == AiAssistantTurnState.Pending).Select(turn => (DateTime?)turn.SubmittedAtUtc).MinAsync(cancellationToken),
            await turns.Where(turn => turn.ProtectedQuestion != null && (turn.ContentExpiresAtUtc <= nowUtc || turn.Conversation.State == AiConversationState.Removed
                    || dbContext.Users.IgnoreQueryFilters().Any(user => user.Id == turn.OwnerUserId && user.IsDeleted)))
                .Select(turn => (DateTime?)(turn.Conversation.RemovalRequestedAtUtc
                    ?? dbContext.Users.IgnoreQueryFilters().Where(user => user.Id == turn.OwnerUserId && user.IsDeleted)
                        .Select(user => user.DeletedAtUtc).FirstOrDefault() ?? turn.ContentExpiresAtUtc)).MinAsync(cancellationToken),
            await dbContext.Set<AiAssistantRequestReceipt>().AsNoTracking().CountAsync(receipt => receipt.AcceptedDayUtc == acceptedDayUtc, cancellationToken));
    }

    public Task<AiConversationRow?> FindConversationAsync(Guid ownerUserId, Guid conversationId, CancellationToken cancellationToken) =>
        ProjectConversations(dbContext.Set<AiConversation>().AsNoTracking()
            .Where(conversation => conversation.OwnerUserId == ownerUserId && conversation.Id == conversationId && conversation.ChannelId == "home")).SingleOrDefaultAsync(cancellationToken);
    public async Task<AiAssistantTurnRow?> FindTurnAsync(Guid turnId, CancellationToken cancellationToken)
    {
        var row = await ProjectTurns(dbContext.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.Id == turnId)).SingleOrDefaultAsync(cancellationToken);
        return await AttachToolResultsAsync(row, cancellationToken);
    }
    public async Task<AiAssistantTurnRow?> FindTurnByRequestAsync(Guid ownerUserId, Guid requestId, CancellationToken cancellationToken)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var row = await ProjectTurns(dbContext.Set<AiAssistantTurn>().AsNoTracking().IgnoreQueryFilters().Where(turn => turn.OwnerUserId == ownerUserId
            && dbContext.Set<AiAssistantRequestReceipt>().Any(receipt => receipt.OwnerUserId == ownerUserId
                && receipt.RequestId == requestId && receipt.TurnId == turn.Id && receipt.ExpiresAtUtc > nowUtc)))
            .SingleOrDefaultAsync(cancellationToken);
        return await AttachToolResultsAsync(row, cancellationToken);
    }

    private async Task<AiAssistantTurnRow?> AttachToolResultsAsync(AiAssistantTurnRow? row, CancellationToken cancellationToken)
    {
        if (row?.ProtectedQuestion is null) return row;
        var tools = await dbContext.Set<AiAssistantToolExecution>().AsNoTracking().Where(tool => tool.TurnId == row.Id)
            .OrderBy(tool => tool.Ordinal)
            .Select(tool => new AiAssistantToolResultRow(tool.Ordinal, tool.ToolId, tool.Version, tool.ProtectedResult))
            .ToArrayAsync(cancellationToken);
        return row with { ToolResults = tools };
    }

    public Task<PagedResult<AiConversationRow>> ListConversationsAsync(Guid ownerUserId, ListAiConversationsRequest request, CancellationToken cancellationToken) =>
        ProjectConversations(dbContext.Set<AiConversation>().AsNoTracking().Where(conversation => conversation.OwnerUserId == ownerUserId && conversation.ChannelId == "home")
            .ApplySort(SortDescriptor.Parse(request.Sort), ConversationSortMap, new("lastActivityAtUtc", true), conversation => conversation.Id))
            .ToPagedResultAsync(request, cancellationToken);
    public Task<PagedResult<AiAssistantTurnRow>> ListTurnsAsync(Guid ownerUserId, Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken) =>
        ProjectTurns(dbContext.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.OwnerUserId == ownerUserId && turn.ConversationId == conversationId)
            .ApplySort(SortDescriptor.Parse(request.Sort), TurnSortMap, new("ordinal", false), turn => turn.Id))
            .ToPagedResultAsync(request, cancellationToken);

    private IQueryable<AiConversationRow> ProjectConversations(IQueryable<AiConversation> query) =>
        from conversation in query
        join question in dbContext.Set<AiAssistantTurn>().AsNoTracking().Where(turn => turn.Ordinal == 1)
            on new { ConversationId = conversation.Id, conversation.OwnerUserId }
            equals new { question.ConversationId, question.OwnerUserId } into questions
        from question in questions.DefaultIfEmpty()
        // The unique ConversationId/Ordinal index guarantees one row without a global window/sort.
        select new AiConversationRow(conversation.Id, conversation.OwnerUserId, conversation.State, conversation.Revision,
            conversation.LastActivityAtUtc, conversation.RequiredPermissions,
            dbContext.Set<AiAssistantTurn>().Where(turn => turn.ConversationId == conversation.Id
                && (turn.State == AiAssistantTurnState.Pending || turn.State == AiAssistantTurnState.Running))
                .Select(turn => (Guid?)turn.RequestId).FirstOrDefault(),
            question == null ? null : new AiConversationQuestionRow(question.Id, question.PayloadRevision, question.ProtectedQuestion, question.ContentExpiresAtUtc));

    private static IQueryable<AiAssistantTurnRow> ProjectTurns(IQueryable<AiAssistantTurn> query) => query.Select(turn =>
        new AiAssistantTurnRow(turn.Id, turn.OwnerUserId, turn.ConversationId, turn.RequestId, turn.Ordinal,
            turn.State, turn.Stage, turn.Revision, turn.Conversation.Revision, turn.Conversation.RequiredPermissions,
            turn.Conversation.State == AiConversationState.Active ? turn.ProtectedQuestion : null,
            turn.Conversation.State == AiConversationState.Active ? turn.ProtectedAnswer : null,
            turn.Conversation.State == AiConversationState.Active ? turn.ProtectedEvidences : null, turn.PayloadRevision,
            turn.Culture, turn.TimeZoneId, turn.Provider, turn.Model, turn.ConnectionRevision,
            turn.AttemptId, turn.Fence, turn.SubmittedAtUtc, turn.ContentExpiresAtUtc, turn.DeadlineAtUtc,
            turn.CancelRequestedAtUtc, turn.ProviderSteps, turn.ToolCalls, turn.ToolBytes,
            turn.InputTokens, turn.OutputTokens, turn.UsageUnknown, turn.FailureCode)
        { AudioSeconds = turn.AudioSeconds, ChannelId = turn.Conversation.ChannelId, ExecutionProfileJson = turn.ExecutionProfileJson });
}
