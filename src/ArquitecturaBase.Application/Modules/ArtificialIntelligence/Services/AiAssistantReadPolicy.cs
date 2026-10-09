using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Channels;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Resources;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantReadPolicy(IAiAssistantReader reader, IAiConnectionReader connectionReader, IRequestValidator validator, IPermissionService permissions, AiAssistantAccessGuard guard, AiAssistantResponsePolicy responsePolicy, IEnumerable<ISystemQuerySource> sources, AiAssistantLimits limits)
{
    public async Task<Result<AiAssistantBootstrapResponse>> BootstrapAsync(CancellationToken cancellationToken)
    {
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var connection = await connectionReader.FindAsync(cancellationToken);
        var current = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var suggestions = sources.SelectMany(s => s.Definitions).Where(d => d.RequiredPermissions.All(current.Contains))
            .Select(d => new AiAssistantSuggestion(d.Id, AssistantTexts.Get("Suggestion." + d.Id))).ToArray();
        var availability = guard.Availability(connection);
        if (availability == "available")
        {
            var workload = await reader.FindWorkloadAsync(responsePolicy.ServerNowUtc, limits.LeaseGraceSeconds, cancellationToken);
            if (workload.Pending >= limits.PendingCapacity) availability = "capacityLimited";
        }
        return new AiAssistantBootstrapResponse(availability, connection.Revision, suggestions,
            new AiAssistantPublicLimits(limits.QuestionCharacters), current.Contains(ArtificialIntelligencePermissions.Read) ? "aiConfiguration" : null);
    }

    public async Task<Result<PagedResult<AiConversationSummaryResponse>>> ListConversationsAsync(ListAiConversationsRequest request, CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var page = await reader.ListConversationsAsync(userId, request, cancellationToken);
        var current = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var items = new List<AiConversationSummaryResponse>();
        foreach (var row in page.Items)
        {
            var canRead = row.RequiredPermissions.All(current.Contains);
            var title = responsePolicy.MapTitle(row, canRead);
            if (title.IsFailure) return title.Error;
            items.Add(new(row.Id, row.Revision, title.Value, row.LastActivityAtUtc, canRead ? "allowed" : "blocked"));
        }
        return new PagedResult<AiConversationSummaryResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<AiConversationResponse>> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
    {
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var row = await reader.FindConversationAsync(userId, conversationId, cancellationToken);
        if (row is null) return AiAssistantErrors.NotFound;
        var canRead = await guard.CanReadAsync(userId, row.RequiredPermissions, cancellationToken);
        var title = responsePolicy.MapTitle(row, canRead);
        if (title.IsFailure) return title.Error;
        return new AiConversationResponse(row.Id, row.Revision, title.Value,
            canRead ? "allowed" : "blocked",
            row.ActiveRequestId, row.LastActivityAtUtc);
    }

    public async Task<Result<PagedResult<AiAssistantTurnResponse>>> ListTurnsAsync(Guid conversationId, ListAiAssistantTurnsRequest request, CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var conversation = await reader.FindConversationAsync(userId, conversationId, cancellationToken);
        if (conversation is null) return AiAssistantErrors.NotFound;
        var canRead = await guard.CanReadAsync(userId, conversation.RequiredPermissions, cancellationToken);
        var page = await reader.ListTurnsAsync(userId, conversationId, request, cancellationToken);
        var items = new List<AiAssistantTurnResponse>();
        foreach (var row in page.Items)
        {
            var mapped = responsePolicy.Map(row, canRead, limits.PollingAfterSeconds);
            if (mapped.IsFailure) return mapped.Error;
            items.Add(mapped.Value);
        }
        return new PagedResult<AiAssistantTurnResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<AiAssistantTurnResponse>> GetTurnAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var row = await reader.FindTurnByRequestAsync(userId, requestId, cancellationToken);
        if (row is null) return AiAssistantErrors.NotFound;
        return responsePolicy.Map(row, await guard.CanReadAsync(userId, row.RequiredPermissions, cancellationToken), limits.PollingAfterSeconds);
    }

}
