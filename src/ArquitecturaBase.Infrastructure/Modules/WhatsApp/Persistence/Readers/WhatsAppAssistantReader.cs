using ArquitecturaBase.Application.Models.StructuredData;
using System.Text.Json;
using ArquitecturaBase.Application.Modules.WhatsApp.Services;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;

internal sealed class WhatsAppAssistantReader(ApplicationDbContext db,IOptions<WhatsAppMessageRetentionOptions> retention,TimeProvider clock) : IWhatsAppAssistantReader
{
    private IQueryable<WhatsAppAssistantRequest> References()=>db.Set<WhatsAppAssistantRequest>().AsNoTracking();
    private IQueryable<WhatsAppMessage> BusinessInbound()=>db.Set<WhatsAppMessage>().AsNoTracking().Where(message=>message.Direction==WhatsAppMessageDirection.Inbound && message.Purpose!=WhatsAppMessagePurpose.Access);
    // Review of task 9 of the documents: a tap on a document question that was only answered (not a form chosen that continues the
    // operation) is no turn of the conversation, so the latest message is the one before it and an interpretation in course goes on.
    private IQueryable<WhatsAppMessage> ConversationInbound()=>BusinessInbound().Where(message=>!(message.Kind==WhatsAppMessageKind.ButtonReply
        && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantDocumentReplyCodec.Prefix+"%") && message.AssistantProcessedAtUtc!=null
        && !db.Set<WhatsAppAssistantRequest>().Any(reference=>reference.ContactId==message.ContactId && reference.SourceMessageId==message.Id
            && reference.PreviousRequestId!=null)));
    // A stored spreadsheet (IA, Stage 2) is a source with or without its caption while its copy and its prepared sheet last.
    private IQueryable<WhatsAppMessage> Sources()=>BusinessInbound().Where(message=>message.ProcessedAtUtc!=null
        && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
        && ((message.Kind==WhatsAppMessageKind.Text || message.Kind==WhatsAppMessageKind.Audio && message.AudioState==WhatsAppAudioState.Completed
                && message.AudioContentExpiresAtUtc>clock.GetUtcNow().UtcDateTime
                // Casos del asistente: a tap on a case's button reads as its title; a fixed text or a handoff is answered before.
                || message.Kind==WhatsAppMessageKind.ButtonReply && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantCaseButtonCodec.Prefix+"%")
                // Task 9 of the documents: a form chosen for a document that was not recognized, once its continuation carries the document; a
                // tap that was only answered is no source, so it never replaces an interpretation in course.
                || message.Kind==WhatsAppMessageKind.ButtonReply && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantDocumentReplyCodec.Prefix+"%")
                    && db.Set<WhatsAppAssistantRequest>().Any(reference=>reference.ContactId==message.ContactId && reference.SourceMessageId==message.Id
                        && reference.AttachmentMessageId!=null && reference.PreviousRequestId!=null))
                && !string.IsNullOrWhiteSpace(message.Body)
            || message.Kind==WhatsAppMessageKind.FlowReply && message.FlowCaptureState==WhatsAppFlowCaptureState.Completed && message.FlowRecordId!=null
            || message.Kind==WhatsAppMessageKind.Spreadsheet && message.SpreadsheetState==WhatsAppSpreadsheetState.Stored && message.SpreadsheetStorageKey!=null
                && message.SpreadsheetSize!=null && message.SpreadsheetCopySha256!=null && message.SpreadsheetPreparedJson!=null
            // Task 9 of the documents: a read document, with its copy and its reading, like a stored spreadsheet.
            || message.Kind==WhatsAppMessageKind.ReceivedDocument && message.DocumentState==WhatsAppDocumentState.Read && message.DocumentStorageKey!=null
                && message.DocumentSize!=null && message.DocumentCopySha256!=null && message.DocumentReadingJson!=null));
    private IQueryable<WhatsAppAssistantSourceRow> SourceRows(IQueryable<WhatsAppMessage> messages)=>messages.Select(message=>new WhatsAppAssistantSourceRow(
        message.Id,message.ContactId!.Value,message.Kind==WhatsAppMessageKind.FlowReply?string.Empty:message.Body??string.Empty,message.OccurredAtUtc,
        message.Kind==WhatsAppMessageKind.Audio && message.AudioContentExpiresAtUtc<message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays)
            ? message.AudioContentExpiresAtUtc!.Value : message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays))
        {CapturedRecordId=message.Kind==WhatsAppMessageKind.FlowReply?message.FlowRecordId:null,WaMessageId=message.WaMessageId,AssistantProcessedAtUtc=message.AssistantProcessedAtUtc,
            Kind=message.Kind,InboundSequence=message.InboundSequence,
            Spreadsheet=message.Kind==WhatsAppMessageKind.Spreadsheet && message.SpreadsheetState==WhatsAppSpreadsheetState.Stored && message.SpreadsheetStorageKey!=null
                && message.SpreadsheetSize!=null && message.SpreadsheetCopySha256!=null && message.SpreadsheetPreparedJson!=null
                ? new WhatsAppAssistantSpreadsheetRow(message.Id,message.SpreadsheetStorageKey,message.SpreadsheetFileName??string.Empty,message.SpreadsheetSize.Value,
                    message.SpreadsheetCopySha256,message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays),message.SpreadsheetPreparedJson) : null,
            Document=message.Kind==WhatsAppMessageKind.ReceivedDocument && message.DocumentState==WhatsAppDocumentState.Read && message.DocumentStorageKey!=null
                && message.DocumentSize!=null && message.DocumentCopySha256!=null && message.DocumentReadingJson!=null
                ? new WhatsAppAssistantDocumentRow(message.Id,message.DocumentStorageKey,message.DocumentFileName??string.Empty,message.DocumentSize.Value,
                    message.DocumentCopySha256,message.DocumentPages,message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays),message.DocumentReadingJson)
                    {ReceivedAtUtc=message.OccurredAtUtc} : null});
    // IA, Stage 2: the business message just before a source, for a spreadsheet to pair with its contiguous text or audio: a text's body,
    // a completed audio's transcription while it lasts, a spreadsheet's caption, state and copy, and whether an assistant reply (one
    // that did not fail) was requested after it and before the source. Meta stamps the messages and the server the replies.
    private IQueryable<PreviousRow> PreviousRows(WhatsAppAssistantSourceRow source)=>
        BusinessInbound().Where(message=>message.ContactId==source.ContactId && message.InboundSequence<source.InboundSequence)
            .OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id)
            .Select(message=>new PreviousRow(message.Id,message.Kind,message.OccurredAtUtc,message.Body,message.AudioState,message.AudioContentExpiresAtUtc,
                message.SpreadsheetState,message.SpreadsheetStorageKey,message.SpreadsheetFileName,message.SpreadsheetSize,message.SpreadsheetCopySha256,
                message.SpreadsheetPreparedJson,
                db.Set<WhatsAppOutgoingMessage>().Any(outgoing=>outgoing.ContactId==source.ContactId && outgoing.Origin==WhatsAppMessageOrigin.Assistant
                    && outgoing.State!=WhatsAppOutgoingMessageState.Failed && outgoing.RequestedAtUtc>message.OccurredAtUtc && outgoing.RequestedAtUtc<source.OccurredAtUtc))
            {
                DocumentState=message.DocumentState,DocumentFailureCode=message.DocumentFailureCode,DocumentStorageKey=message.DocumentStorageKey,DocumentFileName=message.DocumentFileName,
                DocumentSize=message.DocumentSize,DocumentCopySha256=message.DocumentCopySha256,DocumentPages=message.DocumentPages,DocumentReadingJson=message.DocumentReadingJson,
            });
    private sealed record PreviousRow(Guid Id,WhatsAppMessageKind Kind,DateTime OccurredAtUtc,string? Body,WhatsAppAudioState? AudioState,
        DateTime? AudioContentExpiresAtUtc,WhatsAppSpreadsheetState? SpreadsheetState,string? SpreadsheetStorageKey,string? SpreadsheetFileName,
        long? SpreadsheetSize,string? SpreadsheetCopySha256,string? SpreadsheetPreparedJson,bool AnsweredSince)
    {
        // Task 9 of the documents: a received document's state (and why it is unavailable) and, read, its copy and its reading.
        public WhatsAppDocumentState? DocumentState {get;init;}
        public string? DocumentFailureCode {get;init;}
        public string? DocumentStorageKey {get;init;}
        public string? DocumentFileName {get;init;}
        public long? DocumentSize {get;init;}
        public string? DocumentCopySha256 {get;init;}
        public int? DocumentPages {get;init;}
        public string? DocumentReadingJson {get;init;}
        public override string ToString()=>"PreviousRow { Content = [REDACTED] }";
    }
    private async Task<WhatsAppAssistantNeighborRow?> FindPreviousAsync(WhatsAppAssistantSourceRow source,CancellationToken ct)
    {
        var nowUtc=clock.GetUtcNow().UtcDateTime;var days=retention.Value.MessageRetentionDays;
        var previous=await PreviousRows(source).FirstOrDefaultAsync(ct);
        if(previous is null)return null;
        var expiresAtUtc=previous.OccurredAtUtc.AddDays(days);
        var audible=previous.Kind==WhatsAppMessageKind.Audio && previous.AudioState==WhatsAppAudioState.Completed && previous.AudioContentExpiresAtUtc>nowUtc;
        if(audible && previous.AudioContentExpiresAtUtc<expiresAtUtc)expiresAtUtc=previous.AudioContentExpiresAtUtc!.Value;
        var live=expiresAtUtc>nowUtc;
        return new(previous.Id,previous.Kind,previous.OccurredAtUtc,expiresAtUtc)
        {
            Body=live && (previous.Kind is WhatsAppMessageKind.Text or WhatsAppMessageKind.Spreadsheet or WhatsAppMessageKind.ReceivedDocument || audible) ? previous.Body : null,
            AudioState=previous.AudioState,SpreadsheetState=previous.SpreadsheetState,AnsweredSince=previous.AnsweredSince,DocumentState=previous.DocumentState,
            DocumentFailureCode=previous.DocumentFailureCode,
            Document=live && previous.Kind==WhatsAppMessageKind.ReceivedDocument && previous.DocumentState==WhatsAppDocumentState.Read
                && previous.DocumentStorageKey is {} documentKey && previous.DocumentSize is {} documentSize && previous.DocumentCopySha256 is {} documentSha256
                && previous.DocumentReadingJson is {} reading
                ? new(previous.Id,documentKey,previous.DocumentFileName??string.Empty,documentSize,documentSha256,previous.DocumentPages,previous.OccurredAtUtc.AddDays(days),reading)
                    {ReceivedAtUtc=previous.OccurredAtUtc} : null,
            Spreadsheet=live && previous.Kind==WhatsAppMessageKind.Spreadsheet && previous.SpreadsheetState==WhatsAppSpreadsheetState.Stored
                && previous.SpreadsheetStorageKey is {} key && previous.SpreadsheetSize is {} size && previous.SpreadsheetCopySha256 is {} sha256
                && previous.SpreadsheetPreparedJson is {} prepared
                ? new(previous.Id,key,previous.SpreadsheetFileName??string.Empty,size,sha256,previous.OccurredAtUtc.AddDays(days),prepared) : null,
        };
    }
    private async Task<WhatsAppAssistantSourceRow?> ResolveAsync(WhatsAppAssistantSourceRow? source,CancellationToken ct)
    {
        if(source is null || source.CapturedRecordId is not null) return source;
        // Task 9 of the documents: a form chosen for a document that was not recognized continues with the document its reference carries.
        if(source.Kind==WhatsAppMessageKind.ButtonReply && await References().Where(reference=>reference.ContactId==source.ContactId
                && reference.SourceMessageId==source.Id && reference.AttachmentMessageId!=null && reference.PreviousRequestId!=null)
                .Select(reference=>reference.AttachmentMessageId).FirstOrDefaultAsync(ct) is {} attachment)
            return await FindDocumentAsync(source.ContactId,attachment,ct) is {} document
                ? source with {Document=document,ContentExpiresAtUtc=document.ExpiresAtUtc<source.ContentExpiresAtUtc?document.ExpiresAtUtc:source.ContentExpiresAtUtc}
                : source;
        return WhatsAppAssistantSourcePolicy.Resolve(source,await FindPreviousAsync(source,ct));
    }
    // W1-A: a later message of the same send (its skip button, the Flow after the location) has another RequestId: the outgoing
    // messages of its session, found first by their index, name the reference's own.
    public async Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid outgoingId,CancellationToken ct)
    {
        var sessionId=await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(item=>item.Id==outgoingId).Select(item=>item.FlowSessionId).FirstOrDefaultAsync(ct);
        List<Guid> ids=sessionId is {} session
            ? await db.Set<WhatsAppOutgoingMessage>().AsNoTracking().Where(item=>item.FlowSessionId==session).Select(item=>item.Id).ToListAsync(ct)
            : [outgoingId];
        return await ReferenceRows(References().Where(row=>row.OutgoingId!=null && ids.Contains(row.OutgoingId.Value) || row.QuestionOutgoingId==outgoingId)).SingleOrDefaultAsync(ct);
    }
    private static IQueryable<WhatsAppAssistantReferenceRow> ReferenceRows(IQueryable<WhatsAppAssistantRequest> rows)=>rows.Select(row=>new WhatsAppAssistantReferenceRow(row.RequestId,row.ContactId,row.SourceMessageId,
        row.ActorUserId,row.SubjectUserId,row.Kind,row.ModeRevision,row.LinkRevision,row.ContextJson,row.ContentExpiresAtUtc,row.State,row.ProposalId,row.DefinitionId,row.OutgoingId)
        {QuestionOutgoingId=row.QuestionOutgoingId,SourceRecordId=row.SourceRecordId,SuggestionSetId=row.SuggestionSetId,SuggestionId=row.SuggestionId,CapturedCulture=row.CapturedCulture,IsAlways=row.IsAlways,
            PreviousRequestId=row.PreviousRequestId,AttachmentMessageId=row.AttachmentMessageId});
    public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        ReferenceRows(References().Where(row=>row.ContactId==contactId && row.RequestId==requestId)).SingleOrDefaultAsync(ct);
    public Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId,CancellationToken ct)=>CurrentReferenceRows(contactId)
        .Select(row=>new WhatsAppAssistantSuggestionPointerResponse(row.RequestId,row.SourceMessageId,row.State)).SingleOrDefaultAsync(ct);
    private IQueryable<WhatsAppAssistantRequest> CurrentReferenceRows(Guid contactId)=>
        from row in References()
         join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.ContactId==contactId && row.Kind==WhatsAppAssistantRequestKind.Manual && contact.Mode==WhatsAppConversationMode.Manual
            && row.ModeRevision==contact.ModeRevision && row.LinkRevision==contact.LinkRevision && row.SubjectUserId==contact.UserId
            && row.ContentExpiresAtUtc>clock.GetUtcNow().UtcDateTime
            && (row.SourceMessageId==BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
                || row.SourceRecordId!=null && BusinessInbound().Any(message=>message.ContactId==contactId
                    && message.Id==BusinessInbound().Where(latest=>latest.ContactId==contactId).OrderByDescending(latest=>latest.InboundSequence).ThenByDescending(latest=>latest.Id).Select(latest=>(Guid?)latest.Id).FirstOrDefault()
                    && message.Kind==WhatsAppMessageKind.FlowReply && message.ProcessedAtUtc!=null && message.FlowCaptureState==WhatsAppFlowCaptureState.Completed && message.FlowRecordId==row.SourceRecordId))
         select row;
    public async Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid sourceMessageId,CancellationToken ct)=>
        await ResolveAsync(await SourceRows(Sources().Where(message=>message.ContactId==contactId && message.Id==sourceMessageId)).SingleOrDefaultAsync(ct),ct);
    public Task<WhatsAppAssistantChoiceRow?> FindLatestChoiceAsync(Guid contactId,CancellationToken ct)
    {
        var latest=BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence)
            .ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).Take(1);
        return BusinessInbound().Where(message=>message.ContactId==contactId && latest.Contains(message.Id)
                && message.Kind==WhatsAppMessageKind.ButtonReply && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null
                && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
                && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantReplyCodec.Prefix+"%"))
            .Select(message=>new WhatsAppAssistantChoiceRow(message.Id,message.WaMessageId,message.ReplyId!,message.OccurredAtUtc)).SingleOrDefaultAsync(ct);
    }
    public Task<WhatsAppAssistantChoiceRow?> FindLatestAuthoringTapAsync(Guid contactId,CancellationToken ct)
    {
        var latest=BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence)
            .ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).Take(1);
        return BusinessInbound().Where(message=>message.ContactId==contactId && latest.Contains(message.Id)
                && message.Kind==WhatsAppMessageKind.ButtonReply && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null
                && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
                && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantAuthoringReplyCodec.Prefix+"%"))
            .Select(message=>new WhatsAppAssistantChoiceRow(message.Id,message.WaMessageId,message.ReplyId!,message.OccurredAtUtc)).SingleOrDefaultAsync(ct);
    }
    // Task 9 of the documents: the read document a reference carries, by its message, whatever pairs with it now.
    public Task<WhatsAppAssistantDocumentRow?> FindDocumentAsync(Guid contactId,Guid messageId,CancellationToken ct)=>
        BusinessInbound().Where(message=>message.ContactId==contactId && message.Id==messageId && message.Kind==WhatsAppMessageKind.ReceivedDocument
                && message.DocumentState==WhatsAppDocumentState.Read && message.DocumentStorageKey!=null && message.DocumentSize!=null
                && message.DocumentCopySha256!=null && message.DocumentReadingJson!=null
                && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays))
            .Select(message=>new WhatsAppAssistantDocumentRow(message.Id,message.DocumentStorageKey!,message.DocumentFileName??string.Empty,message.DocumentSize!.Value,
                message.DocumentCopySha256!,message.DocumentPages,message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays),message.DocumentReadingJson!)
                {ReceivedAtUtc=message.OccurredAtUtc}).SingleOrDefaultAsync(ct);
    // Review of task 9: a tap on a document question changes something only in the person's automatic conversation with the question's
    // link, before its content expires, and once: another tap on the same question that got its own reference already answered it.
    public async Task<WhatsAppAssistantDocumentTapState> FindDocumentTapStateAsync(WhatsAppAssistantReferenceRow question,Guid tapId,DateTime nowUtc,CancellationToken ct)
    {
        var open=question.ContentExpiresAtUtc>nowUtc && await db.Set<WhatsAppContact>().AsNoTracking().AnyAsync(contact=>contact.Id==question.ContactId
            && contact.Mode==WhatsAppConversationMode.Assistant && contact.WaId!=null && contact.UserId==question.SubjectUserId
            && contact.LinkRevision==question.LinkRevision,ct);
        if(!open) return WhatsAppAssistantDocumentTapState.Closed;
        var prefix=WhatsAppAssistantDocumentReplyCodec.Prefix+question.RequestId.ToString("N")+":";
        // Closing task 9: only a tap whose choice was applied answered it: one that continues the operation, or one whose reply went out
        // naming the question's proposal. What could not be applied («ya cambió», the team has it, it could not be prepared) names its own
        // reference as the proposal, and a reply that could not be accepted is Blocked: neither closes the question.
        var answered=await (from reference in References()
                join tap in BusinessInbound() on reference.SourceMessageId equals tap.Id
                where reference.ContactId==question.ContactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic && tap.Id!=tapId
                    && tap.Kind==WhatsAppMessageKind.ButtonReply && tap.ReplyId!=null && tap.ReplyId.StartsWith(prefix)
                    && (reference.PreviousRequestId!=null || reference.State==WhatsAppAssistantRequestState.Sent && reference.ProposalId!=reference.RequestId)
                select reference.Id).AnyAsync(ct);
        return answered?WhatsAppAssistantDocumentTapState.Answered:WhatsAppAssistantDocumentTapState.Open;
    }
    // Task 9: the latest inbound, only if it is a tap on an option of a document question not yet attended.
    public Task<WhatsAppAssistantChoiceRow?> FindLatestDocumentTapAsync(Guid contactId,CancellationToken ct)
    {
        var latest=BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence)
            .ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).Take(1);
        return BusinessInbound().Where(message=>message.ContactId==contactId && latest.Contains(message.Id)
                && message.Kind==WhatsAppMessageKind.ButtonReply && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null
                && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
                && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantDocumentReplyCodec.Prefix+"%"))
            .Select(message=>new WhatsAppAssistantChoiceRow(message.Id,message.WaMessageId,message.ReplyId!,message.OccurredAtUtc)).SingleOrDefaultAsync(ct);
    }
    // Task 9: the operation of the latest document question (its own outgoing with options of a document), the only one whose options count.
    // The options are JSON, read here: of the latest questions that carry a document, the first whose options are a document's.
    public async Task<Guid?> FindLatestDocumentRequestAsync(Guid contactId,CancellationToken ct)
    {
        var questions=await (from reference in References()
             join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.OutgoingId equals outgoing.Id
             where reference.ContactId==contactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic && reference.AttachmentMessageId!=null
                && (outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies || outgoing.Kind==WhatsAppOutgoingMessageKind.List)
                && outgoing.Origin==WhatsAppMessageOrigin.Assistant && outgoing.QuickRepliesJson!=null
             orderby reference.CreatedAtUtc descending,reference.Id descending
             select new { reference.RequestId, outgoing.QuickRepliesJson }).Take(20).ToListAsync(ct);
        return questions.FirstOrDefault(question=>question.QuickRepliesJson!.Contains(WhatsAppAssistantDocumentReplyCodec.Prefix,StringComparison.Ordinal))?.RequestId;
    }
    public Task<WhatsAppAssistantChoiceRow?> FindLatestCaseTapAsync(Guid contactId,CancellationToken ct)
    {
        var latest=BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence)
            .ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).Take(1);
        return BusinessInbound().Where(message=>message.ContactId==contactId && latest.Contains(message.Id)
                && message.Kind==WhatsAppMessageKind.ButtonReply && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null
                && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
                && message.ReplyId!=null && EF.Functions.Like(message.ReplyId,WhatsAppAssistantCaseButtonCodec.Prefix+"%"))
            .Select(message=>new WhatsAppAssistantChoiceRow(message.Id,message.WaMessageId,message.ReplyId!,message.OccurredAtUtc)).SingleOrDefaultAsync(ct);
    }
    // A summary is the automatic reply whose own outgoing has buttons and no why of the agent: a clarification keeps them in its
    // question, and a case's question («Preguntar con botones») carries the why of its reply. A document question (task 9 of the
    // documents) is no summary either: it carries its document and its buttons are a document's, read here (the options are JSON) among
    // the latest twenty.
    public async Task<Guid?> FindLatestAuthoringRequestAsync(Guid contactId,CancellationToken ct)
    {
        var latest=await (from reference in References()
             join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.OutgoingId equals outgoing.Id
             where reference.ContactId==contactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic
                && outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies && outgoing.Origin==WhatsAppMessageOrigin.Assistant
                && outgoing.AssistantReplyJson==null
             orderby reference.CreatedAtUtc descending,reference.Id descending
             select new {reference.RequestId,reference.AttachmentMessageId,outgoing.QuickRepliesJson}).Take(20).ToListAsync(ct);
        return latest.FirstOrDefault(row=>row.AttachmentMessageId is null
            || row.QuickRepliesJson?.Contains(WhatsAppAssistantDocumentReplyCodec.Prefix,StringComparison.Ordinal)!=true)?.RequestId;
    }
    // A spreadsheet's rejection answers with a fixed text that names its own request as the proposal: it is no turn of the conversation.
    public Task<Guid?> FindChangeRequestAsync(Guid contactId,CancellationToken ct)=>
        References().Where(reference=>reference.ContactId==contactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic
                && reference.ProposalId!=reference.RequestId)
            .OrderByDescending(reference=>reference.CreatedAtUtc).ThenByDescending(reference=>reference.Id)
            .Select(reference=>reference.ChangeRequestId).FirstOrDefaultAsync(ct);
    private static IQueryable<WhatsAppMessage> CurrentSourceMessages(IQueryable<WhatsAppMessage> messages)
    {
        var latest=messages.OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Take(1);
        return from recent in latest
            from source in messages
            where recent.Kind==WhatsAppMessageKind.FlowReply && recent.ProcessedAtUtc!=null && recent.FlowCaptureState==WhatsAppFlowCaptureState.Completed && recent.FlowRecordId!=null
                ? source.Id==messages.Where(first=>first.Kind==WhatsAppMessageKind.FlowReply && first.ProcessedAtUtc!=null
                    && first.FlowCaptureState==WhatsAppFlowCaptureState.Completed && first.FlowRecordId==recent.FlowRecordId)
                    .OrderBy(first=>first.InboundSequence).ThenBy(first=>first.Id).Select(first=>(Guid?)first.Id).FirstOrDefault()
                : source.Id==recent.Id
            select source;
    }
    public async Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId,CancellationToken ct)
    {
        var latest=CurrentSourceMessages(ConversationInbound().Where(message=>message.ContactId==contactId)).Select(message=>message.Id);
        return await ResolveAsync(await SourceRows(Sources().Where(message=>message.ContactId==contactId && latest.Contains(message.Id))).SingleOrDefaultAsync(ct),ct);
    }
    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
        await PendingContactRows(excluded,limit,nowUtc).ToListAsync(ct);
    // A source still waiting for its turn (a burst of the agent) also counts, so the processor looks again soon.
    public async Task<bool> ExistsPendingAsync(DateTime nowUtc,CancellationToken ct)=>
        await PendingReferences(nowUtc).AnyAsync(ct) || await PendingContactRows([],1,nowUtc).AnyAsync(ct);
    private IQueryable<WhatsAppAssistantRequest> PendingReferences(DateTime nowUtc)=>
        References().Where(row=>row.State==WhatsAppAssistantRequestState.Pending && row.ContentExpiresAtUtc>nowUtc);
    private IQueryable<Guid> PendingContactRows(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc)
    {
        var pending=PendingReferences(nowUtc).Select(row=>row.ContactId);
        // Closing task 9: the latest message of the conversation, as FindLatestSourceAsync takes it: a tap on a document question that was only
        // answered is no turn, so a text sent before it (whose burst still waited when the tap was answered) is found again.
        var automatic=from contact in db.Set<WhatsAppContact>().AsNoTracking()
            let latest=ConversationInbound().Where(message=>message.ContactId==contact.Id).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
            where contact.Mode==WhatsAppConversationMode.Assistant && latest!=null
                && (Sources().Any(message=>message.Id==latest && message.Kind!=WhatsAppMessageKind.FlowReply && message.AssistantProcessedAtUtc==null)
                    || BusinessInbound().Any(message=>message.Id==latest && message.Kind==WhatsAppMessageKind.ButtonReply
                        && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null && message.ReplyId!=null
                        && EF.Functions.Like(message.ReplyId,WhatsAppAssistantReplyCodec.Prefix+"%")
                        && message.OccurredAtUtc>nowUtc.AddDays(-retention.Value.MessageRetentionDays))
                        && References().Any(row=>row.ContactId==contact.Id && row.Kind==WhatsAppAssistantRequestKind.Automatic
                            && row.State==WhatsAppAssistantRequestState.Ready && row.QuestionOutgoingId!=null)
                    || BusinessInbound().Any(message=>message.Id==latest && message.Kind==WhatsAppMessageKind.ButtonReply
                        && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null && message.ReplyId!=null
                        && EF.Functions.Like(message.ReplyId,WhatsAppAssistantAuthoringReplyCodec.Prefix+"%")
                        && message.OccurredAtUtc>nowUtc.AddDays(-retention.Value.MessageRetentionDays))
                    // Task 9 of the documents: a tap on an option of a document question.
                    || BusinessInbound().Any(message=>message.Id==latest && message.Kind==WhatsAppMessageKind.ButtonReply
                        && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null && message.ReplyId!=null
                        && EF.Functions.Like(message.ReplyId,WhatsAppAssistantDocumentReplyCodec.Prefix+"%")
                        && message.OccurredAtUtc>nowUtc.AddDays(-retention.Value.MessageRetentionDays)))
                && !References().Any(row=>row.ContactId==contact.Id && row.SourceMessageId==latest && row.Kind==WhatsAppAssistantRequestKind.Automatic)
            select contact.Id;
        var captured=from contact in db.Set<WhatsAppContact>().AsNoTracking()
            let latest=ConversationInbound().Where(receipt=>receipt.ContactId==contact.Id)
                .OrderByDescending(receipt=>receipt.InboundSequence).ThenByDescending(receipt=>receipt.Id).FirstOrDefault()
            from message in Sources()
            where latest!=null && latest.Kind==WhatsAppMessageKind.FlowReply && latest.ProcessedAtUtc!=null
                && latest.FlowCaptureState==WhatsAppFlowCaptureState.Completed && latest.FlowRecordId!=null
                && message.ContactId==contact.Id && message.Id==BusinessInbound().Where(first=>first.ContactId==contact.Id
                    && first.Kind==WhatsAppMessageKind.FlowReply && first.ProcessedAtUtc!=null
                    && first.FlowCaptureState==WhatsAppFlowCaptureState.Completed && first.FlowRecordId==latest.FlowRecordId)
                    .OrderBy(first=>first.InboundSequence).ThenBy(first=>first.Id).Select(first=>(Guid?)first.Id).FirstOrDefault()
                && message.AssistantProcessedAtUtc==null && message.FlowRecordId!=null
                && !References().Any(reference=>reference.ContactId==contact.Id && reference.SourceRecordId==message.FlowRecordId)
            select contact.Id;
        return pending.Union(automatic).Union(captured).Where(id=>!excluded.Contains(id)).OrderBy(id=>id).Take(limit);
    }
    // A load that answers a file the person sent (a document or a spreadsheet, the reference carries it) is not an offer either.
    public Task<int> CountOffersAsync(Guid contactId,DateTime fromUtc,DateTime toUtc,CancellationToken ct,Guid? excludingRequestId=null)=>
        References().CountAsync(reference=>reference.ContactId==contactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic
            && reference.RequestId!=excludingRequestId && reference.AttachmentMessageId==null && db.Set<WhatsAppOutgoingMessage>().Any(outgoing=>
                // A summary of a form or catalog to build (its own outgoing with buttons) is not an offer (IA3-A).
                // W1-A: a send with a location opens with its request, which is the offer; its later Flow is not another one.
                (outgoing.Id==reference.OutgoingId && (outgoing.Kind==WhatsAppOutgoingMessageKind.Flow || outgoing.Kind==WhatsAppOutgoingMessageKind.LocationRequest)
                    || outgoing.Id==reference.QuestionOutgoingId && outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies)
                && outgoing.Origin==WhatsAppMessageOrigin.Assistant
                && outgoing.State!=WhatsAppOutgoingMessageState.Failed && outgoing.RequestedAtUtc>=fromUtc && outgoing.RequestedAtUtc<toUtc),ct);
    public Task<bool> ExistsCurrentContextAsync(WhatsAppAssistantReferenceRow reference,DateTime nowUtc,CancellationToken ct)=>
        (from row in References()
         join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.RequestId==reference.RequestId && row.ContactId==reference.ContactId && row.SourceMessageId==reference.SourceMessageId
            && row.State==WhatsAppAssistantRequestState.Pending && row.ContentExpiresAtUtc>nowUtc
            && row.ActorUserId==reference.ActorUserId && row.SubjectUserId==reference.SubjectUserId
            && contact.UserId==row.SubjectUserId && contact.LinkRevision==row.LinkRevision && contact.ModeRevision==row.ModeRevision
            && contact.Mode==(row.Kind==WhatsAppAssistantRequestKind.Automatic?WhatsAppConversationMode.Assistant:WhatsAppConversationMode.Manual)
            && contact.WaId!=null && contact.LastInboundAtUtc>nowUtc.AddHours(-24)
            && row.SourceMessageId==ConversationInbound().Where(message=>message.ContactId==contact.Id)
                .OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
         select row.Id).AnyAsync(ct);
    // A spreadsheet's rejection (its fixed text names its own request as the proposal) is no offer.
    public Task<bool> ExistsUnresolvedOfferAsync(Guid contactId,Guid excludingRequestId,CancellationToken ct)=>
        db.Set<WhatsAppOutgoingMessage>().AsNoTracking().AnyAsync(outgoing=>outgoing.ContactId==contactId
            && outgoing.Origin==WhatsAppMessageOrigin.Assistant
            && (outgoing.State==WhatsAppOutgoingMessageState.Pending || outgoing.State==WhatsAppOutgoingMessageState.Sending || outgoing.State==WhatsAppOutgoingMessageState.Uncertain)
            && !References().Any(reference=>reference.RequestId==excludingRequestId
                && (reference.OutgoingId==outgoing.Id || reference.QuestionOutgoingId==outgoing.Id
                    || outgoing.FlowSessionId!=null && db.Set<WhatsAppOutgoingMessage>().Any(first=>first.Id==reference.OutgoingId && first.FlowSessionId==outgoing.FlowSessionId)))
            && !References().Any(reference=>reference.OutgoingId==outgoing.Id && reference.ProposalId==reference.RequestId),ct);
    public Task<bool> ExistsAttachmentAnswerAsync(Guid contactId,Guid attachmentMessageId,CancellationToken ct)=>
        References().AnyAsync(row=>row.ContactId==contactId && row.AttachmentMessageId==attachmentMessageId && row.ActionOrdinal==0
            && (row.OutgoingId!=null || row.QuestionOutgoingId!=null),ct);

    public Task<bool> ExistsDeclinedAsync(Guid contactId,Guid definitionId,CancellationToken ct)=>
        (from row in References() join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.ContactId==contactId && row.SubjectUserId==contact.UserId && row.LinkRevision==contact.LinkRevision && row.DeclinedDefinitionId==definitionId
         select row.Id).AnyAsync(ct);
    public async Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId, CancellationToken ct)
    {
        var sinceUtc = clock.GetUtcNow().UtcDateTime.AddHours(-24);
        var flows = await (from outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            join session in db.Set<WhatsAppFlowSession>().AsNoTracking() on outgoing.FlowSessionId equals session.Id
            join contact in db.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
            where contact.UserId == subjectUserId && session.UserId == subjectUserId && session.LinkRevision == contact.LinkRevision
                // W1-A: one per send: its location request, or the Flow of a send without one; its resend (W3) is not another offer.
                && (outgoing.Kind == WhatsAppOutgoingMessageKind.Flow && session.LocationState == WhatsAppFlowLocationState.None && !outgoing.IsResend
                    || outgoing.Kind == WhatsAppOutgoingMessageKind.LocationRequest) && outgoing.State != WhatsAppOutgoingMessageState.Failed
                && outgoing.RequestedAtUtc >= sinceUtc
            orderby outgoing.RequestedAtUtc descending, outgoing.Id descending
            select new { OutgoingId = outgoing.Id, outgoing.RequestedAtUtc, outgoing.RequestId, session.DefinitionId,
                session.VersionId, session.NameJson, session.Culture }).Take(20).ToListAsync(ct);
        var questions = await QuestionRows(subjectUserId, sinceUtc).Take(20).ToListAsync(ct);
        var flowOffers = flows.Select(row => (row.RequestedAtUtc, row.OutgoingId, Index: 0,
            Offer: new StructuredFormOfferReference(row.RequestId, row.DefinitionId, row.VersionId,
                JsonSerializer.Deserialize<Dictionary<string, string>>(row.NameJson, JsonSerializerOptions.Web)![row.Culture])));
        var quickOffers = questions.SelectMany(row => ReadQuickReplies(row.RequestId, row.QuickRepliesJson)
            .Select((offer, index) => (row.RequestedAtUtc, row.OutgoingId, Index: index, Offer: offer)));
        return flowOffers.Concat(quickOffers).OrderByDescending(row => row.RequestedAtUtc).ThenByDescending(row => row.OutgoingId)
            .ThenBy(row => row.Index).Take(20).Select(row => row.Offer).ToArray();
    }

    public async Task<IReadOnlyList<Guid>> ListDeclinedDefinitionIdsAsync(Guid subjectUserId,
        IReadOnlyCollection<Guid> definitionIds, CancellationToken ct)
    {
        if (definitionIds.Count == 0) return [];
        return await (from reference in References()
            join contact in db.Set<WhatsAppContact>().AsNoTracking() on reference.ContactId equals contact.Id
            where contact.UserId == subjectUserId && reference.SubjectUserId == subjectUserId
                && reference.LinkRevision == contact.LinkRevision && reference.DeclinedDefinitionId != null
                && definitionIds.Contains(reference.DeclinedDefinitionId.Value)
            select reference.DeclinedDefinitionId!.Value).Distinct().OrderBy(id => id).ToArrayAsync(ct);
    }
    private IQueryable<QuickOfferRow> QuestionRows(Guid subjectUserId, DateTime sinceUtc) =>
        from reference in References()
        join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.QuestionOutgoingId equals outgoing.Id
        join contact in db.Set<WhatsAppContact>().AsNoTracking() on reference.ContactId equals contact.Id
        where reference.SubjectUserId==subjectUserId && contact.UserId==subjectUserId && reference.LinkRevision==contact.LinkRevision
            && outgoing.ContactId==contact.Id && outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies
            && outgoing.Origin==WhatsAppMessageOrigin.Assistant && outgoing.State!=WhatsAppOutgoingMessageState.Failed && outgoing.RequestedAtUtc >= sinceUtc
        orderby outgoing.RequestedAtUtc descending,outgoing.Id descending
        select new QuickOfferRow(outgoing.Id,contact.Id,reference.RequestId,outgoing.RequestedAtUtc,outgoing.QuickRepliesJson!);

    internal static StructuredFormOfferReference[] ReadQuickReplies(Guid requestId,string json)
    {
        WhatsAppOutgoingQuickReply[] replies;
        try { replies=JsonSerializer.Deserialize<WhatsAppOutgoingQuickReply[]>(json,JsonSerializerOptions.Web)
                ?? throw new InvalidOperationException("The stored assistant question has no options."); }
        catch(JsonException exception) { throw new InvalidOperationException("The stored assistant question is invalid.",exception); }
        if(replies.Length is <2 or >3) throw new InvalidOperationException("The stored assistant question has an invalid option count.");
        var offers=new StructuredFormOfferReference[replies.Length]; var definitions=new HashSet<Guid>();
        for(var index=0;index<replies.Length;index++)
        {
            var reply=replies[index];
            if(reply is null || !WhatsAppAssistantReplyCodec.TryParse(reply.Id,out var observedRequest,out var definitionId,out var versionId)
                || observedRequest!=requestId || string.IsNullOrWhiteSpace(reply.Title) || reply.Title.Length>20
                || reply.DefinitionRevision<=0 || !definitions.Add(definitionId))
                throw new InvalidOperationException("The stored assistant question contains an invalid option.");
            offers[index]=new(requestId,definitionId,versionId,reply.Title);
        }
        return offers;
    }

    private sealed record QuickOfferRow(Guid OutgoingId,Guid ContactId,Guid RequestId,DateTime RequestedAtUtc,string QuickRepliesJson);

    public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        (from reference in References()
         join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.OutgoingId equals outgoing.Id
         where reference.ContactId==contactId && reference.RequestId==requestId
         select new WhatsAppOutgoingMessageRow(outgoing.Id,outgoing.RequestId,outgoing.OperatorUserId,outgoing.Body,outgoing.RequestedAtUtc,outgoing.State,outgoing.WaMessageId,outgoing.Kind,
            outgoing.FlowSession==null?null:new WhatsAppFormPresentationRow(outgoing.FlowSession.DefinitionId,outgoing.FlowSession.VersionId,outgoing.FlowSession.VersionNumber,outgoing.FlowSession.NameJson,outgoing.FlowSession.Culture),outgoing.Origin)
         { Assistant = outgoing.FlowSession != null && outgoing.FlowSession.ProtectedAssistantContent != null && outgoing.FlowSession.AssistantContentExpiresAtUtc != null
                    ? new WhatsAppAssistantPresentationRow(outgoing.FlowSession.Id,outgoing.FlowSession.ContactId,outgoing.FlowSession.UserId,outgoing.FlowSession.VersionId,outgoing.FlowSession.ProtectedAssistantContent,outgoing.FlowSession.AssistantContentExpiresAtUtc.Value) : null, QuickRepliesJson = outgoing.QuickRepliesJson }).SingleOrDefaultAsync(ct);
}
