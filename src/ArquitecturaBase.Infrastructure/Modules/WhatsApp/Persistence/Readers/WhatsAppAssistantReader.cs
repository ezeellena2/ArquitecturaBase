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
    private IQueryable<WhatsAppMessage> Sources()=>BusinessInbound().Where(message=>message.ProcessedAtUtc!=null
        && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
        && ((message.Kind==WhatsAppMessageKind.Text || message.Kind==WhatsAppMessageKind.Audio && message.AudioState==WhatsAppAudioState.Completed
                && message.AudioContentExpiresAtUtc>clock.GetUtcNow().UtcDateTime) && !string.IsNullOrWhiteSpace(message.Body)
            || message.Kind==WhatsAppMessageKind.FlowReply && message.FlowCaptureState==WhatsAppFlowCaptureState.Completed && message.FlowRecordId!=null));
    private IQueryable<WhatsAppAssistantSourceRow> SourceRows(IQueryable<WhatsAppMessage> messages)=>messages.Select(message=>new WhatsAppAssistantSourceRow(
        message.Id,message.ContactId!.Value,message.Kind==WhatsAppMessageKind.FlowReply?string.Empty:message.Body!,message.OccurredAtUtc,
        message.Kind==WhatsAppMessageKind.Audio && message.AudioContentExpiresAtUtc<message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays)
            ? message.AudioContentExpiresAtUtc!.Value : message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays))
        {CapturedRecordId=message.Kind==WhatsAppMessageKind.FlowReply?message.FlowRecordId:null,WaMessageId=message.WaMessageId});
    public Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid outgoingId,CancellationToken ct)=>
        ReferenceRows(References().Where(row=>row.OutgoingId==outgoingId || row.QuestionOutgoingId==outgoingId)).SingleOrDefaultAsync(ct);
    private static IQueryable<WhatsAppAssistantReferenceRow> ReferenceRows(IQueryable<WhatsAppAssistantRequest> rows)=>rows.Select(row=>new WhatsAppAssistantReferenceRow(row.RequestId,row.ContactId,row.SourceMessageId,
        row.ActorUserId,row.SubjectUserId,row.Kind,row.ModeRevision,row.LinkRevision,row.ContextJson,row.ContentExpiresAtUtc,row.State,row.ProposalId,row.DefinitionId,row.OutgoingId)
        {QuestionOutgoingId=row.QuestionOutgoingId,SourceRecordId=row.SourceRecordId,SuggestionSetId=row.SuggestionSetId,SuggestionId=row.SuggestionId,CapturedCulture=row.CapturedCulture,IsAlways=row.IsAlways});
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
    public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid sourceMessageId,CancellationToken ct)=>
        SourceRows(Sources().Where(message=>message.ContactId==contactId && message.Id==sourceMessageId)).SingleOrDefaultAsync(ct);
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
    public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId,CancellationToken ct)
    {
        var latest=CurrentSourceMessages(BusinessInbound().Where(message=>message.ContactId==contactId)).Select(message=>message.Id);
        return SourceRows(Sources().Where(message=>message.ContactId==contactId && latest.Contains(message.Id))).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
        await PendingContactRows(excluded,limit,nowUtc).ToListAsync(ct);
    private IQueryable<Guid> PendingContactRows(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc)
    {
        var pending=References().Where(row=>row.State==WhatsAppAssistantRequestState.Pending && row.ContentExpiresAtUtc>nowUtc).Select(row=>row.ContactId);
        var automatic=from contact in db.Set<WhatsAppContact>().AsNoTracking()
            let latest=BusinessInbound().Where(message=>message.ContactId==contact.Id).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
            where contact.Mode==WhatsAppConversationMode.Assistant && latest!=null
                && (Sources().Any(message=>message.Id==latest && message.Kind!=WhatsAppMessageKind.FlowReply)
                    || BusinessInbound().Any(message=>message.Id==latest && message.Kind==WhatsAppMessageKind.ButtonReply
                        && message.ProcessedAtUtc!=null && message.AssistantProcessedAtUtc==null && message.ReplyId!=null
                        && EF.Functions.Like(message.ReplyId,WhatsAppAssistantReplyCodec.Prefix+"%")
                        && message.OccurredAtUtc>nowUtc.AddDays(-retention.Value.MessageRetentionDays))
                        && References().Any(row=>row.ContactId==contact.Id && row.Kind==WhatsAppAssistantRequestKind.Automatic
                            && row.State==WhatsAppAssistantRequestState.Ready && row.QuestionOutgoingId!=null))
                && !References().Any(row=>row.ContactId==contact.Id && row.SourceMessageId==latest && row.Kind==WhatsAppAssistantRequestKind.Automatic)
            select contact.Id;
        var captured=from contact in db.Set<WhatsAppContact>().AsNoTracking()
            let latest=BusinessInbound().Where(message=>message.ContactId==contact.Id).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
            from message in Sources()
            where message.ContactId==contact.Id && message.Id==latest && message.Kind==WhatsAppMessageKind.FlowReply
                && message.AssistantProcessedAtUtc==null && message.FlowRecordId!=null
                && !References().Any(reference=>reference.ContactId==contact.Id && reference.SourceRecordId==message.FlowRecordId)
            select contact.Id;
        return pending.Union(automatic).Union(captured).Where(id=>!excluded.Contains(id)).OrderBy(id=>id).Take(limit);
    }
    public Task<int> CountOffersAsync(Guid contactId,DateTime fromUtc,DateTime toUtc,CancellationToken ct,Guid? excludingRequestId=null)=>
        References().CountAsync(reference=>reference.ContactId==contactId && reference.Kind==WhatsAppAssistantRequestKind.Automatic
            && reference.RequestId!=excludingRequestId && db.Set<WhatsAppOutgoingMessage>().Any(outgoing=>
                (outgoing.Id==reference.OutgoingId || outgoing.Id==reference.QuestionOutgoingId)
                && outgoing.Origin==WhatsAppMessageOrigin.Assistant
                && (outgoing.Kind==WhatsAppOutgoingMessageKind.Flow || outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies)
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
            && row.SourceMessageId==BusinessInbound().Where(message=>message.ContactId==contact.Id)
                .OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
         select row.Id).AnyAsync(ct);
    public Task<bool> ExistsUnresolvedOfferAsync(Guid contactId,Guid excludingRequestId,CancellationToken ct)=>
        db.Set<WhatsAppOutgoingMessage>().AsNoTracking().AnyAsync(outgoing=>outgoing.ContactId==contactId
            && outgoing.Origin==WhatsAppMessageOrigin.Assistant
            && (outgoing.State==WhatsAppOutgoingMessageState.Pending || outgoing.State==WhatsAppOutgoingMessageState.Sending || outgoing.State==WhatsAppOutgoingMessageState.Uncertain)
            && !References().Any(reference=>reference.RequestId==excludingRequestId
                && (reference.OutgoingId==outgoing.Id || reference.QuestionOutgoingId==outgoing.Id)),ct);
    public Task<bool> ExistsDeclinedAsync(Guid contactId,Guid definitionId,CancellationToken ct)=>
        (from row in References() join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.ContactId==contactId && row.SubjectUserId==contact.UserId && row.LinkRevision==contact.LinkRevision && row.DeclinedDefinitionId==definitionId
         select row.Id).AnyAsync(ct);
    public async Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId,CancellationToken ct)
    {
        var flows=await (from outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            join session in db.Set<WhatsAppFlowSession>().AsNoTracking() on outgoing.FlowSessionId equals session.Id
            join contact in db.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
            where contact.UserId==subjectUserId && session.UserId==subjectUserId && session.LinkRevision==contact.LinkRevision
                && outgoing.Kind==WhatsAppOutgoingMessageKind.Flow && outgoing.State!=WhatsAppOutgoingMessageState.Failed
            orderby outgoing.RequestedAtUtc descending,outgoing.Id descending
            select new{OutgoingId=outgoing.Id,outgoing.RequestedAtUtc,outgoing.RequestId,session.DefinitionId,session.VersionId,session.NameJson,session.Culture,
                Declined=References().Any(decline=>decline.ContactId==contact.Id && decline.SubjectUserId==subjectUserId
                    && decline.LinkRevision==contact.LinkRevision && decline.DeclinedDefinitionId==session.DefinitionId)}).Take(101).ToListAsync(ct);
        var questionRows=await QuestionRows(subjectUserId).Take(101).ToListAsync(ct);
        var noDeclines=new HashSet<Guid>();
        var questions=questionRows.Select(row=>new{Row=row,Offers=ReadQuickReplies(row.RequestId,row.QuickRepliesJson,noDeclines)}).ToArray();
        var contactIds=questionRows.Select(row=>row.ContactId).Distinct().ToArray();
        var definitionIds=questions.SelectMany(question=>question.Offers).Select(offer=>offer.DefinitionId).Distinct().ToArray();
        var declinedPairs=new HashSet<(Guid ContactId,Guid DefinitionId)>();
        if(contactIds.Length>0)
        {
            var declined=await
            (from reference in References()
             join contact in db.Set<WhatsAppContact>().AsNoTracking() on reference.ContactId equals contact.Id
             where contactIds.Contains(contact.Id) && contact.UserId==subjectUserId && reference.SubjectUserId==subjectUserId
                && reference.LinkRevision==contact.LinkRevision && reference.DeclinedDefinitionId!=null
                && definitionIds.Contains(reference.DeclinedDefinitionId.Value)
             select new{reference.ContactId,DefinitionId=reference.DeclinedDefinitionId!.Value}).Distinct().ToListAsync(ct);
            foreach(var row in declined) declinedPairs.Add((row.ContactId,row.DefinitionId));
        }
        var flowOffers=flows.Select(row=>(row.RequestedAtUtc,row.OutgoingId,Index:0,Offer:new StructuredFormOfferReference(row.RequestId,row.DefinitionId,row.VersionId,
            JsonSerializer.Deserialize<Dictionary<string,string>>(row.NameJson,JsonSerializerOptions.Web)![row.Culture],row.Declined)));
        var quickOffers=questions.SelectMany(question=>question.Offers.Select((offer,index)=>(question.Row.RequestedAtUtc,question.Row.OutgoingId,Index:index,
            Offer:offer with{Declined=declinedPairs.Contains((question.Row.ContactId,offer.DefinitionId))})));
        // The 101st row is a completeness sentinel consumed by the capability policy.
        return flowOffers.Concat(quickOffers).OrderByDescending(row=>row.RequestedAtUtc).ThenByDescending(row=>row.OutgoingId)
            .ThenBy(row=>row.Index).Take(101).Select(row=>row.Offer).ToArray();
    }
    private IQueryable<QuickOfferRow> QuestionRows(Guid subjectUserId) =>
        from reference in References()
        join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.QuestionOutgoingId equals outgoing.Id
        join contact in db.Set<WhatsAppContact>().AsNoTracking() on reference.ContactId equals contact.Id
        where reference.SubjectUserId==subjectUserId && contact.UserId==subjectUserId && reference.LinkRevision==contact.LinkRevision
            && outgoing.ContactId==contact.Id && outgoing.Kind==WhatsAppOutgoingMessageKind.QuickReplies
            && outgoing.Origin==WhatsAppMessageOrigin.Assistant && outgoing.State!=WhatsAppOutgoingMessageState.Failed
        orderby outgoing.RequestedAtUtc descending,outgoing.Id descending
        select new QuickOfferRow(outgoing.Id,contact.Id,reference.RequestId,outgoing.RequestedAtUtc,outgoing.QuickRepliesJson!);

    internal static StructuredFormOfferReference[] ReadQuickReplies(Guid requestId,string json,IReadOnlySet<Guid> declined)
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
            offers[index]=new(requestId,definitionId,versionId,reply.Title,declined.Contains(definitionId));
        }
        return offers;
    }

    private sealed record QuickOfferRow(Guid OutgoingId,Guid ContactId,Guid RequestId,DateTime RequestedAtUtc,string QuickRepliesJson);

    public async Task<IReadOnlyList<Guid>> ListCapturedSourceRecordIdsAsync(Guid subjectUserId,CancellationToken ct)=>await
        (from session in db.Set<WhatsAppFlowSession>().AsNoTracking()
         join contact in db.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
         where contact.UserId==subjectUserId && session.UserId==subjectUserId && session.LinkRevision==contact.LinkRevision
            && session.CaptureState==WhatsAppFlowCaptureState.Completed && session.CompletedRecordId!=null
         orderby session.CreatedAtUtc descending,session.Id descending
         select session.CompletedRecordId!.Value).Take(101).ToListAsync(ct);

    public Task<WhatsAppOutgoingMessageRow?> FindOutgoingAsync(Guid contactId,Guid requestId,CancellationToken ct)=>
        (from reference in References()
         join outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking() on reference.OutgoingId equals outgoing.Id
         where reference.ContactId==contactId && reference.RequestId==requestId
         select new WhatsAppOutgoingMessageRow(outgoing.Id,outgoing.RequestId,outgoing.OperatorUserId,outgoing.Body,outgoing.RequestedAtUtc,outgoing.State,outgoing.WaMessageId,outgoing.Kind,
            outgoing.FlowSession==null?null:new WhatsAppFormPresentationRow(outgoing.FlowSession.DefinitionId,outgoing.FlowSession.VersionId,outgoing.FlowSession.VersionNumber,outgoing.FlowSession.NameJson,outgoing.FlowSession.Culture),outgoing.Origin)
         { Assistant = outgoing.FlowSession != null && outgoing.FlowSession.ProtectedAssistantContent != null && outgoing.FlowSession.AssistantContentExpiresAtUtc != null
                    ? new WhatsAppAssistantPresentationRow(outgoing.FlowSession.Id,outgoing.FlowSession.ContactId,outgoing.FlowSession.UserId,outgoing.FlowSession.VersionId,outgoing.FlowSession.ProtectedAssistantContent,outgoing.FlowSession.AssistantContentExpiresAtUtc.Value) : null, QuickRepliesJson = outgoing.QuickRepliesJson }).SingleOrDefaultAsync(ct);
}
