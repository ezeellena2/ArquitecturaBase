using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppChatReader(ApplicationDbContext dbContext) : IWhatsAppChatReader
{
    private static readonly IReadOnlyDictionary<string, Expression<Func<WhatsAppChatRow, object?>>> SortMap =
        new Dictionary<string, Expression<Func<WhatsAppChatRow, object?>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["lastInboundAtUtc"] = item => item.LastInboundAtUtc,
            ["profileName"] = item => item.ProfileName,
        };

    public Task<PagedResult<WhatsAppChatRow>> ListChatsAsync(ListWhatsAppChatsRequest request, Guid operatorUserId, CancellationToken cancellationToken)
    {
        var query = Chats(request.Search, operatorUserId);
        if (request.UnreadOnly) { query = query.Where(item => item.UnreadCount > 0); }
        return query.ApplySort(SortDescriptor.Parse(request.Sort), SortMap, new SortDescriptor("lastInboundAtUtc", true), item => item.Id)
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<WhatsAppChatCounts> CountChatsAsync(ListWhatsAppChatsRequest request, Guid operatorUserId, CancellationToken cancellationToken)
    {
        var query = Chats(request.Search, operatorUserId);
        return new(await query.CountAsync(cancellationToken), await query.CountAsync(item => item.UnreadCount > 0, cancellationToken));
    }

    public async Task<WhatsAppThreadRow?> FindThreadAsync(ListWhatsAppMessagesRequest request, Guid operatorUserId, CancellationToken cancellationToken)
    {
        var contact = await Chats(null, operatorUserId).SingleOrDefaultAsync(item => item.Id == request.ContactId, cancellationToken);
        if (contact is null) { return null; }
        var query = dbContext.Set<WhatsAppMessage>().AsNoTracking().Where(item => item.ContactId == request.ContactId);
        if (request.BeforeAtUtc is { } at && request.BeforeId is { } id)
        {
            query = query.Where(item => item.OccurredAtUtc < at || (item.OccurredAtUtc == at && item.Id.CompareTo(id) < 0));
        }

        var items = await query.OrderByDescending(item => item.OccurredAtUtc).ThenByDescending(item => item.Id)
            .Take(request.Limit + 1).Select(item => new WhatsAppMessageRow(item.Id, item.Direction, item.Kind, item.Purpose == WhatsAppMessagePurpose.Access ? null : item.Body,
                item.OccurredAtUtc, item.InboundSequence, item.OperatorUserId, item.Status, item.ErrorCode, item.Purpose, item.AccessEventKind,
                item.Kind == WhatsAppMessageKind.Flow && item.FlowSession != null
                    ? new WhatsAppFormPresentationRow(item.FlowSession.DefinitionId, item.FlowSession.VersionId,
                        item.FlowSession.VersionNumber, item.FlowSession.NameJson, item.FlowSession.Culture)
                    : null,
                item.Kind == WhatsAppMessageKind.FlowReply
                    ? item.FlowSession == null ? new WhatsAppFormReplyRow(item.FlowCaptureState == WhatsAppFlowCaptureState.Rejected ? "Rejected" : item.ProcessedAtUtc == null ? item.HasFlowFiles ? item.FlowReplyFailureCount > 0 ? "RetryingFiles" : "Downloading" : "Pending" : "Unrecognized", null, null, null, null, null, null, null, null,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Rejected ? item.FlowCaptureErrorCode : null, null, item.FlowFileCount)
                    : new WhatsAppFormReplyRow(item.FlowCaptureState.HasValue ? item.FlowCaptureState.Value.ToString()
                        : item.ProcessedAtUtc == null ? item.HasFlowFiles ? item.FlowReplyFailureCount > 0 ? "RetryingFiles" : "Downloading" : "Pending" : "Unrecognized", item.FlowSession.Id,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Unrecognized ? null : item.FlowSession.DefinitionId,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Unrecognized ? null : item.FlowSession.VersionId,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Unrecognized ? null : item.FlowSession.VersionNumber,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Unrecognized ? null : item.FlowSession.NameJson,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Unrecognized ? null : item.FlowSession.Culture,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Completed ? item.FlowRecordId : null,
                        item.FlowCaptureState == WhatsAppFlowCaptureState.Completed ? item.FlowRecordNumber : null,
                        item.FlowCaptureErrorCode, item.FlowRejectedQuestion, item.FlowFileCount)
                    : null, item.Origin)).ToListAsync(cancellationToken);
        var hasPrevious = items.Count > request.Limit;
        if (hasPrevious) { items.RemoveAt(items.Count - 1); }
        items.Reverse();
        var first = items.FirstOrDefault();
        var outgoing = await Outgoing().Where(item => item.ContactId == request.ContactId && item.State != WhatsAppOutgoingMessageState.Accepted)
            .OrderByDescending(item => item.RequestedAtUtc).ThenByDescending(item => item.Id).Take(50)
            .Select(item => new WhatsAppOutgoingMessageRow(item.Id, item.RequestId, item.OperatorUserId, item.Body,
                item.RequestedAtUtc, item.State, item.WaMessageId, item.Kind,
                item.Kind == WhatsAppOutgoingMessageKind.Flow && item.FlowSession != null
                    ? new WhatsAppFormPresentationRow(item.FlowSession.DefinitionId, item.FlowSession.VersionId,
                        item.FlowSession.VersionNumber, item.FlowSession.NameJson, item.FlowSession.Culture)
                    : null, item.Origin)).ToListAsync(cancellationToken);
        return new(contact, items, outgoing, hasPrevious ? first?.OccurredAtUtc : null, hasPrevious ? first?.Id : null);
    }

    public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId, Guid requestId, Guid operatorUserId, CancellationToken cancellationToken) =>
        Outgoing().Where(item => item.ContactId == contactId && item.RequestId == requestId && item.OperatorUserId == operatorUserId)
            .Select(item => new WhatsAppOutgoingMessageRow(item.Id, item.RequestId, item.OperatorUserId, item.Body,
                item.RequestedAtUtc, item.State, item.WaMessageId, item.Kind,
                item.Kind == WhatsAppOutgoingMessageKind.Flow && item.FlowSession != null
                    ? new WhatsAppFormPresentationRow(item.FlowSession.DefinitionId, item.FlowSession.VersionId,
                        item.FlowSession.VersionNumber, item.FlowSession.NameJson, item.FlowSession.Culture)
                    : null, item.Origin)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<WhatsAppOutgoingMessage> Outgoing() => dbContext.Set<WhatsAppOutgoingMessage>().AsNoTracking();

    private IQueryable<WhatsAppChatRow> Chats(string? search, Guid actor)
    {
        var contacts = dbContext.Set<WhatsAppContact>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = LikePatterns.Contains(search.Trim());
            var digits = string.Concat(search.Where(char.IsAsciiDigit));
            contacts = contacts.Where(item => (item.ProfileName != null && EF.Functions.ILike(item.ProfileName, pattern, LikePatterns.EscapeCharacter))
                || (digits.Length > 0 && item.WaId != null && item.WaId.Contains(digits)));
        }

        return contacts.Select(contact => new WhatsAppChatRow
        {
            Id = contact.Id, ProfileName = contact.ProfileName, PhoneNumber = contact.WaId,
            Mode = contact.Mode, ModeRevision = contact.ModeRevision, LastInboundAtUtc = contact.LastInboundAtUtc, LastInboundSequence = contact.LastInboundSequence,
            UnreadCount =
            dbContext.Set<WhatsAppMessage>().Count(message => message.ContactId == contact.Id
                && message.Direction == WhatsAppMessageDirection.Inbound
                && message.InboundSequence > (dbContext.Set<WhatsAppReadState>()
                    .Where(read => read.ContactId == contact.Id && read.OperatorUserId == actor)
                    .Select(read => (long?)read.LastReadSequence).FirstOrDefault() ?? 0)),
            Preview =
            dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Purpose == WhatsAppMessagePurpose.Access || message.Origin == WhatsAppMessageOrigin.FormAction ? null : message.Body).FirstOrDefault(),
            PreviewPurpose = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => (WhatsAppMessagePurpose?)message.Purpose).FirstOrDefault() ?? WhatsAppMessagePurpose.Unclassified,
            PreviewIsFormAction = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Origin == WhatsAppMessageOrigin.FormAction).FirstOrDefault(),
            PreviewIsFormReply = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Kind == WhatsAppMessageKind.FlowReply).FirstOrDefault(),
            PreviewAccessEventKind = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.AccessEventKind).FirstOrDefault() });
    }
}
