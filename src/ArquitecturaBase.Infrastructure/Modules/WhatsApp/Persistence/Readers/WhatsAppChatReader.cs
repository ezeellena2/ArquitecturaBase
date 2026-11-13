using System.Linq.Expressions;
using ArquitecturaBase.Application.Common.Pagination;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
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
        if (request.NeedsPersonOnly) { query = query.Where(item => item.HandoffAtUtc != null); }
        return query.ApplySort(SortDescriptor.Parse(request.Sort), SortMap, new SortDescriptor("lastInboundAtUtc", true), item => item.Id)
            .ToPagedResultAsync(request, cancellationToken);
    }

    public async Task<WhatsAppChatCounts> CountChatsAsync(ListWhatsAppChatsRequest request, Guid operatorUserId, CancellationToken cancellationToken)
    {
        var query = Chats(request.Search, operatorUserId);
        return new(await query.CountAsync(cancellationToken), await query.CountAsync(item => item.UnreadCount > 0, cancellationToken),
            await query.CountAsync(item => item.HandoffAtUtc != null, cancellationToken));
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
                (item.Kind == WhatsAppMessageKind.Flow || item.Kind == WhatsAppMessageKind.LocationRequest) && item.FlowSession != null
                    ? new WhatsAppFormPresentationRow(item.FlowSession.DefinitionId, item.FlowSession.VersionId,
                        item.FlowSession.VersionNumber, item.FlowSession.NameJson, item.FlowSession.Culture)
                        { Location = item.Kind == WhatsAppMessageKind.LocationRequest ? new WhatsAppFlowLocationRow(item.FlowSession.LocationState, item.FlowSession.ExpiresAtUtc) : null }
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
                        {
                            // W3 (task 24): a reply rejected while its send already had its response (another reply completed it; a
                            // session's capture is terminal, so that one was processed first) says the number of the one it kept.
                            SavedNumber = item.FlowCaptureState == WhatsAppFlowCaptureState.Rejected && item.FlowSession.CaptureState == WhatsAppFlowCaptureState.Completed
                                && item.FlowSession.ResponseWaMessageId != item.WaMessageId ? item.FlowSession.CompletedNumber : null,
                        }
                    : null, item.Origin, item.Kind == WhatsAppMessageKind.Audio && item.AudioState != null ? new WhatsAppAudioMessageRow(item.AudioState.Value,item.AudioStorageKey != null,item.AudioContentExpiresAtUtc,item.AudioFailureCode) : null)
                // «Por qué lo mandó» goes once, with what was sent when deciding: the location request, or the Flow of a send without one
                // (W1-A); the reply keeps it as before, and a location or a tap that resolved the send takes none, nor a resend (W3).
                { Assistant = item.FlowSession != null && item.FlowSession.ProtectedAssistantContent != null && item.FlowSession.AssistantContentExpiresAtUtc != null
                    && (item.Kind == WhatsAppMessageKind.FlowReply || item.Kind == WhatsAppMessageKind.LocationRequest
                        || item.Kind == WhatsAppMessageKind.Flow && item.FlowSession.LocationState == WhatsAppFlowLocationState.None && !item.IsResend)
                    ? new WhatsAppAssistantPresentationRow(item.FlowSession.Id,item.FlowSession.ContactId,item.FlowSession.UserId,item.FlowSession.VersionId,item.FlowSession.ProtectedAssistantContent,item.FlowSession.AssistantContentExpiresAtUtc.Value) : null, QuickReplyTitlesJson = item.QuickReplyTitlesJson,
                  SpreadsheetName = item.Kind == WhatsAppMessageKind.Spreadsheet ? item.SpreadsheetFileName : null,
                  Latitude = item.Latitude, Longitude = item.Longitude, AssistantReplyJson = item.AssistantReplyJson,
                  // VC1-A (task 29): «Por qué lo propuso» of the notice of the loads of a spreadsheet, while the retention keeps it.
                  SpreadsheetRowsJson = item.SpreadsheetRowsJson,
                  // W3 (task 24): «Prellenado con» of a resend that went out, while the retention keeps what was written.
                  SharedPrefill = item.Kind == WhatsAppMessageKind.Flow && item.IsResend && item.FlowSession != null && item.FlowSession.SharedContentJson != null
                      && item.FlowSession.ProtectedSharedValues != null
                      ? new WhatsAppSharedPrefillRow(item.FlowSession.Id, item.FlowSession.ContactId, item.FlowSession.UserId, item.FlowSession.VersionId,
                          item.FlowSession.SharedContentJson, item.FlowSession.ProtectedSharedValues) : null,
                  // W3: the first card of a shared contact, from its own columns; the address of a place stays out of the chat.
                  ContactName = item.Kind == WhatsAppMessageKind.Contact ? item.ContactName : null,
                  ContactPhonesJson = item.Kind == WhatsAppMessageKind.Contact ? item.ContactPhonesJson : null,
                  // Review of task 9: the form the assistant sent for a file the person sent (its reference carries that file).
                  LoadsFile = item.Kind == WhatsAppMessageKind.Flow && item.FlowSessionId != null && dbContext.Set<WhatsAppOutgoingMessage>().Any(outgoing =>
                      outgoing.FlowSessionId == item.FlowSessionId && dbContext.Set<WhatsAppAssistantRequest>().Any(reference =>
                          reference.OutgoingId == outgoing.Id && reference.AttachmentMessageId != null)),
                  // Review of task 9: a question that went as a list keeps the label of its button from the send it came from, while its
                  // options live.
                  ListButton = item.Origin == WhatsAppMessageOrigin.Assistant && item.Kind == WhatsAppMessageKind.Interactive && item.QuickReplyTitlesJson != null
                      ? dbContext.Set<WhatsAppOutgoingMessage>().Where(sent => sent.ContactId == item.ContactId && sent.WaMessageId == item.WaMessageId
                          && sent.Kind == WhatsAppOutgoingMessageKind.List).Select(sent => sent.ListButton).FirstOrDefault() : null,
                  // Task 8 of the documents: what the chat shows of a received document, from its own columns.
                  Document = item.Kind == WhatsAppMessageKind.ReceivedDocument ? new WhatsAppReceivedDocumentMessageRow(item.DocumentFileName, item.DocumentContentType,
                      item.DocumentStorageKey, item.DocumentPages, item.DocumentState, item.DocumentFailureCode) : null }).ToListAsync(cancellationToken);
        var hasPrevious = items.Count > request.Limit;
        if (hasPrevious) { items.RemoveAt(items.Count - 1); }
        items.Reverse();
        items = await WithDocumentWhyAsync(request.ContactId, items, cancellationToken);
        var first = items.FirstOrDefault();
        var outgoing = await Outgoing().Where(item => item.ContactId == request.ContactId && item.State != WhatsAppOutgoingMessageState.Accepted)
            .OrderByDescending(item => item.RequestedAtUtc).ThenByDescending(item => item.Id).Take(50)
            .Select(OutgoingProjection).ToListAsync(cancellationToken);
        // Review of task 9: the queued forms the assistant sent for a file the person sent, by their send as in the messages: also the Flow
        // that follows its location request, which is what the reference keeps.
        var flowIds = outgoing.Where(item => item.Kind == WhatsAppOutgoingMessageKind.Flow).Select(item => item.Id).ToArray();
        var loading = flowIds.Length == 0 ? [] : await Outgoing()
            .Where(flow => flowIds.Contains(flow.Id) && flow.FlowSessionId != null && dbContext.Set<WhatsAppOutgoingMessage>().Any(opening =>
                opening.FlowSessionId == flow.FlowSessionId && dbContext.Set<WhatsAppAssistantRequest>().Any(reference =>
                    reference.OutgoingId == opening.Id && reference.AttachmentMessageId != null)))
            .Select(flow => flow.Id).ToListAsync(cancellationToken);
        if (loading.Count > 0) outgoing = [.. outgoing.Select(item => loading.Contains(item.Id) ? item with { LoadsFile = true } : item)];
        // Casos del asistente: what the assistant decided without a message, in the span of this page.
        var fromUtc = hasPrevious && first is not null ? first.OccurredAtUtc : DateTime.MinValue;
        var toUtc = request.BeforeAtUtc ?? DateTime.MaxValue;
        var events = await dbContext.Set<WhatsAppAssistantRequest>().AsNoTracking()
            .Where(item => item.ContactId == request.ContactId && item.OutcomeJson != null && item.DecidedAtUtc != null
                && item.DecidedAtUtc >= fromUtc && item.DecidedAtUtc < toUtc)
            .OrderByDescending(item => item.DecidedAtUtc).ThenByDescending(item => item.Id).Take(request.Limit)
            .Select(item => new WhatsAppAssistantEventRow(item.RequestId, item.DecidedAtUtc!.Value, item.OutcomeJson!)).ToListAsync(cancellationToken);
        events.Reverse();
        return new(contact, items, outgoing, hasPrevious ? first?.OccurredAtUtc : null, hasPrevious ? first?.Id : null) { AssistantEvents = events };
    }

    // Task 9 of the documents: «Por qué lo propuso» or «Por qué preguntó» of the received documents of the page, from the latest automatic
    // reference that carries each one and what it sent (a prefilled form or a document question), with the option the person chose.
    private async Task<List<WhatsAppMessageRow>> WithDocumentWhyAsync(Guid contactId, List<WhatsAppMessageRow> items, CancellationToken ct)
    {
        var documentIds = items.Where(item => item.Document is not null).Select(item => item.Id).ToArray();
        if (documentIds.Length == 0) return items;
        var sent = await (from reference in dbContext.Set<WhatsAppAssistantRequest>().AsNoTracking()
                join outgoing in dbContext.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.OutgoingId equals outgoing.Id
                where reference.ContactId == contactId && reference.Kind == WhatsAppAssistantRequestKind.Automatic && reference.AttachmentMessageId != null
                    && documentIds.Contains(reference.AttachmentMessageId.Value)
                    // Review of task 9c: what answers a rejected capture (it names its own request as the proposal) carries the document too,
                    // but the why is the one of the send that proposed the form.
                    && reference.ProposalId != reference.RequestId
                    // A form with a location question opens with its location request, which is what the reference keeps.
                    && ((outgoing.Kind == WhatsAppOutgoingMessageKind.Flow || outgoing.Kind == WhatsAppOutgoingMessageKind.LocationRequest) && outgoing.FlowSession != null
                        || (outgoing.Kind == WhatsAppOutgoingMessageKind.QuickReplies || outgoing.Kind == WhatsAppOutgoingMessageKind.List) && outgoing.QuickRepliesJson != null)
                orderby reference.CreatedAtUtc descending, reference.Id descending
                select new
                {
                    MessageId = reference.AttachmentMessageId!.Value, reference.RequestId, outgoing.Kind, outgoing.QuickRepliesJson,
                    DefinitionId = outgoing.FlowSession != null ? (Guid?)outgoing.FlowSession.DefinitionId : null,
                    FormNameJson = outgoing.FlowSession != null ? outgoing.FlowSession.NameJson : null,
                    FormCulture = outgoing.FlowSession != null ? outgoing.FlowSession.Culture : null,
                    Assistant = outgoing.FlowSession != null && outgoing.FlowSession.ProtectedAssistantContent != null && outgoing.FlowSession.AssistantContentExpiresAtUtc != null
                        ? new WhatsAppAssistantPresentationRow(outgoing.FlowSession.Id, outgoing.FlowSession.ContactId, outgoing.FlowSession.UserId, outgoing.FlowSession.VersionId,
                            outgoing.FlowSession.ProtectedAssistantContent, outgoing.FlowSession.AssistantContentExpiresAtUtc.Value) : null,
                })
            .ToListAsync(ct);
        if (sent.Count == 0) return items;
        var latest = sent.GroupBy(row => row.MessageId).ToDictionary(group => group.Key, group => group.First());
        // The option chosen: a question is answered once, so the first tap on it whose choice was applied, resolved for each question of the
        // page however many taps the person has; the chat names it when read.
        var questions = latest.Values.Where(row => row.Kind is WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List)
            .Select(row => row.RequestId.ToString("N")).Distinct().ToArray();
        var taps = await ListFirstTapsAsync(contactId, questions, ct);
        return items.Select(item =>
        {
            if (item.Document is not { } document || !latest.TryGetValue(item.Id, out var row)) return item;
            var tap = row.Kind is WhatsAppOutgoingMessageKind.QuickReplies or WhatsAppOutgoingMessageKind.List
                ? taps.GetValueOrDefault(row.RequestId.ToString("N")) : null;
            return item with
            {
                Document = document with
                {
                    Why = new WhatsAppReceivedDocumentWhyRow(row.Kind, row.QuickRepliesJson, row.FormNameJson, row.FormCulture, row.Assistant, tap?.Body)
                        { DefinitionId = row.DefinitionId, ChosenReplyId = tap?.ReplyId },
                },
            };
        }).ToList();
    }

    private sealed record DocumentTap(string ReplyId, string? Body);

    // The first tap whose choice was applied on each question asked: the lowest inbound sequence of each one, and then those taps. Closing
    // task 9: applied is a reference that continues the operation or whose reply went out naming the question's proposal; «ya cambió», the
    // team has it, it could not be prepared (they name their own reference) and a reply that could not be accepted (Blocked) are not.
    private async Task<IReadOnlyDictionary<string, DocumentTap>> ListFirstTapsAsync(Guid contactId, string[] questions, CancellationToken ct)
    {
        if (questions.Length == 0) return new Dictionary<string, DocumentTap>();
        var start = WhatsAppAssistantDocumentReplyCodec.Prefix.Length;
        var answered = dbContext.Set<WhatsAppMessage>().AsNoTracking()
            .Where(message => message.ContactId == contactId && message.Direction == WhatsAppMessageDirection.Inbound && message.Kind == WhatsAppMessageKind.ButtonReply
                && message.ReplyId != null && EF.Functions.Like(message.ReplyId, WhatsAppAssistantDocumentReplyCodec.Prefix + "%")
                && questions.Contains(message.ReplyId.Substring(start, 32))
                && dbContext.Set<WhatsAppAssistantRequest>().Any(reference => reference.ContactId == contactId && reference.SourceMessageId == message.Id
                    && (reference.PreviousRequestId != null
                        || reference.State == WhatsAppAssistantRequestState.Sent && reference.ProposalId != reference.RequestId)));
        var firsts = await answered.GroupBy(message => message.ReplyId!.Substring(start, 32)).Select(group => group.Min(message => message.InboundSequence))
            .ToListAsync(ct);
        var rows = await answered.Where(message => firsts.Contains(message.InboundSequence)).OrderBy(message => message.InboundSequence)
            .Select(message => new DocumentTap(message.ReplyId!, message.Body)).ToListAsync(ct);
        return rows.GroupBy(row => row.ReplyId.Substring(start, 32), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
    }

    public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId, Guid requestId, Guid operatorUserId, CancellationToken cancellationToken) =>
        Outgoing().Where(item => item.ContactId == contactId && item.RequestId == requestId && item.OperatorUserId == operatorUserId)
            .Select(OutgoingProjection).SingleOrDefaultAsync(cancellationToken);

    private static readonly Expression<Func<WhatsAppOutgoingMessage,WhatsAppOutgoingMessageRow>> OutgoingProjection =
        item => new(item.Id,item.RequestId,item.OperatorUserId,item.Body,item.RequestedAtUtc,item.State,item.WaMessageId,item.Kind,
            (item.Kind == WhatsAppOutgoingMessageKind.Flow || item.Kind == WhatsAppOutgoingMessageKind.LocationRequest) && item.FlowSession != null
                ? new WhatsAppFormPresentationRow(item.FlowSession.DefinitionId,item.FlowSession.VersionId,item.FlowSession.VersionNumber,item.FlowSession.NameJson,item.FlowSession.Culture)
                    { Location = item.Kind == WhatsAppOutgoingMessageKind.LocationRequest ? new WhatsAppFlowLocationRow(item.FlowSession.LocationState, item.FlowSession.ExpiresAtUtc) : null } : null,item.Origin)
        { Assistant = item.FlowSession != null && item.FlowSession.ProtectedAssistantContent != null && item.FlowSession.AssistantContentExpiresAtUtc != null
                    && (item.Kind == WhatsAppOutgoingMessageKind.LocationRequest
                        || item.Kind == WhatsAppOutgoingMessageKind.Flow && item.FlowSession.LocationState == WhatsAppFlowLocationState.None && !item.IsResend)
                    ? new WhatsAppAssistantPresentationRow(item.FlowSession.Id,item.FlowSession.ContactId,item.FlowSession.UserId,item.FlowSession.VersionId,item.FlowSession.ProtectedAssistantContent,item.FlowSession.AssistantContentExpiresAtUtc.Value) : null, QuickRepliesJson = item.QuickRepliesJson,
            AssistantReplyJson = item.AssistantReplyJson, ListButton = item.Kind == WhatsAppOutgoingMessageKind.List ? item.ListButton : null,
            SpreadsheetRowsJson = item.SpreadsheetRowsJson,
            // W3 (task 24): «Prellenado con» of a queued resend, while the retention keeps what was written.
            SharedPrefill = item.Kind == WhatsAppOutgoingMessageKind.Flow && item.IsResend && item.FlowSession != null && item.FlowSession.SharedContentJson != null
                && item.FlowSession.ProtectedSharedValues != null
                ? new WhatsAppSharedPrefillRow(item.FlowSession.Id, item.FlowSession.ContactId, item.FlowSession.UserId, item.FlowSession.VersionId,
                    item.FlowSession.SharedContentJson, item.FlowSession.ProtectedSharedValues) : null };

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
            HandoffReasonCode = contact.HandoffReasonCode, HandoffReason = contact.HandoffReason, HandoffAtUtc = contact.HandoffAtUtc,
            UnreadCount =
            dbContext.Set<WhatsAppMessage>().Count(message => message.ContactId == contact.Id
                && message.Direction == WhatsAppMessageDirection.Inbound
                && message.InboundSequence > (dbContext.Set<WhatsAppReadState>()
                    .Where(read => read.ContactId == contact.Id && read.OperatorUserId == actor)
                    .Select(read => (long?)read.LastReadSequence).FirstOrDefault() ?? 0)),
            Preview =
            dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Purpose == WhatsAppMessagePurpose.Access || message.Origin == WhatsAppMessageOrigin.FormAction || message.Origin == WhatsAppMessageOrigin.Assistant || message.Kind == WhatsAppMessageKind.Audio || message.Kind == WhatsAppMessageKind.Spreadsheet || message.Kind == WhatsAppMessageKind.ReceivedDocument ? null : message.Body).FirstOrDefault(),
            PreviewPurpose = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => (WhatsAppMessagePurpose?)message.Purpose).FirstOrDefault() ?? WhatsAppMessagePurpose.Unclassified,
            PreviewIsAssistant = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Origin == WhatsAppMessageOrigin.Assistant).FirstOrDefault(),
            PreviewIsFormAction = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.Origin == WhatsAppMessageOrigin.FormAction).FirstOrDefault(),
            // The kind, read once: an audio, a spreadsheet, a received document or a form reply shows a generic preview.
            PreviewKind = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => (WhatsAppMessageKind?)message.Kind).FirstOrDefault(),
            PreviewAccessEventKind = dbContext.Set<WhatsAppMessage>().Where(message => message.ContactId == contact.Id)
                .OrderByDescending(message => message.OccurredAtUtc).ThenByDescending(message => message.Id)
                .Select(message => message.AccessEventKind).FirstOrDefault() });
    }
}
