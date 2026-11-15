using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Common.Validation;
using ArquitecturaBase.Application.Interfaces.Integrations.Identity;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Configuration;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.ArtificialIntelligence.Models;
using ArquitecturaBase.Domain.Modules.ArtificialIntelligence;
using ArquitecturaBase.Domain.Results;

namespace ArquitecturaBase.Application.Modules.ArtificialIntelligence.Services;

internal sealed class AiAssistantReadPolicy(IAiAssistantReader reader, IRequestValidator validator, IPermissionService permissions, AiAssistantAccessGuard guard, AiAssistantResponsePolicy responsePolicy, AiAssistantLimits limits, AiAssistantBootstrapPolicy bootstrap)
{
    // The bootstrap has its own policy: it reads the connection, the catalog and who may attach, and this class stays within eight.
    public Task<Result<AiAssistantBootstrapResponse>> BootstrapAsync(CancellationToken cancellationToken) => bootstrap.BootstrapAsync(cancellationToken);

    public async Task<Result<PagedResult<AiConversationSummaryResponse>>> ListConversationsAsync(ListAiConversationsRequest request, CancellationToken cancellationToken)
    {
        if (await validator.ValidateAsync(request, cancellationToken) is { } error) return error;
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var page = await reader.ListConversationsAsync(userId, request, cancellationToken);
        var current = await permissions.GetPermissionsAsync(userId, cancellationToken);
        var rows = await WithTitleSpreadsheetsAsync(userId, page.Items, row => row.RequiredPermissions.All(current.Contains), cancellationToken);
        var items = new List<AiConversationSummaryResponse>();
        // A conversation whose title cannot be read is marked unavailable and the others are still listed (I8).
        foreach (var row in rows)
        {
            var (title, access) = responsePolicy.Summary(row, row.RequiredPermissions.All(current.Contains));
            items.Add(new(row.Id, row.Revision, title, row.LastActivityAtUtc, access));
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
        row = (await WithTitleSpreadsheetsAsync(userId, [row], _ => canRead, cancellationToken))[0];
        var (title, access) = responsePolicy.Summary(row, canRead);
        return new AiConversationResponse(row.Id, row.Revision, title, access, row.ActiveRequestId, row.LastActivityAtUtc);
    }

    // A first turn without written or dictated text is named after its spreadsheet: only those sealed copies come, in one more query.
    private async Task<IReadOnlyList<AiConversationRow>> WithTitleSpreadsheetsAsync(Guid userId, IReadOnlyList<AiConversationRow> rows,
        Func<AiConversationRow, bool> canRead, CancellationToken cancellationToken)
    {
        var unnamed = rows.Where(row => responsePolicy.NeedsSpreadsheetForTitle(row, canRead(row))).Select(row => row.FirstQuestion!.TurnId).ToArray();
        if (unnamed.Length == 0) return rows;
        var sealedAttachments = await reader.ListProtectedAttachmentsAsync(userId, unnamed, cancellationToken);
        return [.. rows.Select(row => row.FirstQuestion is { } question && sealedAttachments.TryGetValue(question.TurnId, out var protectedAttachment)
            ? row with { FirstQuestion = question with { ProtectedAttachment = protectedAttachment } } : row)];
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
        // Only the turn that brought a spreadsheet shows it, and only when the conversation may be read.
        var rows = canRead ? await AiAssistantAttachmentPolicy.WithOwnAttachmentsAsync(reader, userId, page.Items, cancellationToken) : page.Items;
        // A turn that cannot be read is marked unavailable, without content, and the page still shows the others (I8).
        var items = rows.Select(row => responsePolicy.MapReadable(row, canRead, limits.PollingAfterSeconds)).ToList();
        return new PagedResult<AiAssistantTurnResponse>(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<Result<AiAssistantTurnResponse>> GetTurnAsync(Guid requestId, CancellationToken cancellationToken)
    {
        if (guard.CurrentUserId is not { } userId) return AiAssistantErrors.SessionRequired;
        var actor = await guard.AuthorizeAsync(userId, cancellationToken);
        if (actor.IsFailure) return actor.Error;
        var row = await reader.FindTurnByRequestAsync(userId, requestId, cancellationToken);
        if (row is null || row.ChannelId != "home") return AiAssistantErrors.NotFound;
        var canRead = await guard.CanReadAsync(userId, row.RequiredPermissions, cancellationToken);
        if (canRead) row = (await AiAssistantAttachmentPolicy.WithOwnAttachmentsAsync(reader, userId, [row], cancellationToken))[0];
        return responsePolicy.MapReadable(row, canRead, limits.PollingAfterSeconds);
    }

}
