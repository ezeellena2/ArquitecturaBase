using ArquitecturaBase.Application.Models.StructuredData;
using System.Text.Json;
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
    private IQueryable<WhatsAppMessage> Sources()=>BusinessInbound().Where(message=>message.ProcessedAtUtc!=null && !string.IsNullOrWhiteSpace(message.Body)
        && message.OccurredAtUtc>clock.GetUtcNow().UtcDateTime.AddDays(-retention.Value.MessageRetentionDays)
        && (message.Kind==WhatsAppMessageKind.Text || message.Kind==WhatsAppMessageKind.Audio && message.AudioState==WhatsAppAudioState.Completed
            && message.AudioContentExpiresAtUtc>clock.GetUtcNow().UtcDateTime));
    private IQueryable<WhatsAppAssistantSourceRow> SourceRows(IQueryable<WhatsAppMessage> messages)=>messages.Select(message=>new WhatsAppAssistantSourceRow(
        message.Id,message.ContactId!.Value,message.Body!,message.OccurredAtUtc,
        message.Kind==WhatsAppMessageKind.Audio && message.AudioContentExpiresAtUtc<message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays)
            ? message.AudioContentExpiresAtUtc!.Value : message.OccurredAtUtc.AddDays(retention.Value.MessageRetentionDays)));
    public Task<WhatsAppAssistantReferenceRow?> FindByOutgoingAsync(Guid outgoingId,CancellationToken ct)=>References()
        .Where(row=>row.OutgoingId==outgoingId || row.QuestionOutgoingId==outgoingId).Select(row=>new WhatsAppAssistantReferenceRow(row.RequestId,row.ContactId,row.SourceMessageId,
            row.ActorUserId,row.SubjectUserId,row.Kind,row.ModeRevision,row.LinkRevision,row.ContextJson,row.ContentExpiresAtUtc,row.State,row.ProposalId,row.DefinitionId,row.OutgoingId)).SingleOrDefaultAsync(ct);
    public Task<WhatsAppAssistantReferenceRow?> FindAsync(Guid contactId,Guid requestId,CancellationToken ct)=>References()
        .Where(row=>row.ContactId==contactId && row.RequestId==requestId).Select(row=>new WhatsAppAssistantReferenceRow(row.RequestId,row.ContactId,row.SourceMessageId,
            row.ActorUserId,row.SubjectUserId,row.Kind,row.ModeRevision,row.LinkRevision,row.ContextJson,row.ContentExpiresAtUtc,row.State,row.ProposalId,row.DefinitionId,row.OutgoingId)).SingleOrDefaultAsync(ct);
    public Task<WhatsAppAssistantSuggestionPointerResponse?> FindCurrentAsync(Guid contactId,CancellationToken ct)=>
        (from row in References()
         join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.ContactId==contactId && row.Kind==WhatsAppAssistantRequestKind.Manual && contact.Mode==WhatsAppConversationMode.Manual
            && row.ModeRevision==contact.ModeRevision && row.LinkRevision==contact.LinkRevision && row.SubjectUserId==contact.UserId
            && row.ContentExpiresAtUtc>clock.GetUtcNow().UtcDateTime
            && row.SourceMessageId==BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
         select new WhatsAppAssistantSuggestionPointerResponse(row.RequestId,row.SourceMessageId,row.State)).SingleOrDefaultAsync(ct);
    public Task<WhatsAppAssistantSourceRow?> FindSourceAsync(Guid contactId,Guid sourceMessageId,CancellationToken ct)=>
        SourceRows(Sources().Where(message=>message.ContactId==contactId && message.Id==sourceMessageId)).SingleOrDefaultAsync(ct);
    public Task<WhatsAppAssistantSourceRow?> FindLatestSourceAsync(Guid contactId,CancellationToken ct)
    {
        // Choose the latest business receipt before checking eligibility: a pending/expired audio blocks older text.
        var latest=BusinessInbound().Where(message=>message.ContactId==contactId).OrderByDescending(message=>message.InboundSequence)
            .ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).Take(1);
        return SourceRows(Sources().Where(message=>message.ContactId==contactId && latest.Contains(message.Id))).SingleOrDefaultAsync(ct);
    }
    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)
    {
        var pending=References().Where(row=>row.State==WhatsAppAssistantRequestState.Pending && row.ContentExpiresAtUtc>nowUtc).Select(row=>row.ContactId);
        var automatic=from contact in db.Set<WhatsAppContact>().AsNoTracking()
            let latest=BusinessInbound().Where(message=>message.ContactId==contact.Id).OrderByDescending(message=>message.InboundSequence).ThenByDescending(message=>message.Id).Select(message=>(Guid?)message.Id).FirstOrDefault()
            where contact.Mode==WhatsAppConversationMode.Assistant && latest!=null && Sources().Any(message=>message.Id==latest) && !References().Any(row=>row.ContactId==contact.Id && row.SourceMessageId==latest && row.Kind==WhatsAppAssistantRequestKind.Automatic)
            select contact.Id;
        return await pending.Union(automatic).Where(id=>!excluded.Contains(id)).OrderBy(id=>id).Take(limit).ToListAsync(ct);
    }
    public Task<int> CountOffersAsync(Guid contactId,DateTime fromUtc,DateTime toUtc,CancellationToken ct)=>db.Set<WhatsAppOutgoingMessage>().AsNoTracking().CountAsync(row=>row.ContactId==contactId
        && row.Origin==WhatsAppMessageOrigin.Assistant && row.Kind==WhatsAppOutgoingMessageKind.Flow && row.RequestedAtUtc>=fromUtc && row.RequestedAtUtc<toUtc
        && row.State!=WhatsAppOutgoingMessageState.Failed,ct);
    public Task<bool> ExistsDeclinedAsync(Guid contactId,Guid definitionId,CancellationToken ct)=>
        (from row in References() join contact in db.Set<WhatsAppContact>().AsNoTracking() on row.ContactId equals contact.Id
         where row.ContactId==contactId && row.SubjectUserId==contact.UserId && row.LinkRevision==contact.LinkRevision && row.DeclinedDefinitionId==definitionId
         select row.Id).AnyAsync(ct);
    public async Task<IReadOnlyList<StructuredFormOfferReference>> ListOffersAsync(Guid subjectUserId,CancellationToken ct)
    {
        var rows=await (from outgoing in db.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            join session in db.Set<WhatsAppFlowSession>().AsNoTracking() on outgoing.FlowSessionId equals session.Id
            join contact in db.Set<WhatsAppContact>().AsNoTracking() on session.ContactId equals contact.Id
            where contact.UserId==subjectUserId && session.UserId==subjectUserId && session.LinkRevision==contact.LinkRevision
                && outgoing.Kind==WhatsAppOutgoingMessageKind.Flow && outgoing.State!=WhatsAppOutgoingMessageState.Failed
            orderby outgoing.RequestedAtUtc descending,outgoing.Id descending
            select new{outgoing.RequestId,session.DefinitionId,session.VersionId,session.NameJson,session.Culture,
                Declined=References().Any(decline=>decline.ContactId==contact.Id && decline.SubjectUserId==subjectUserId
                    && decline.LinkRevision==contact.LinkRevision && decline.DeclinedDefinitionId==session.DefinitionId)}).Take(101).ToListAsync(ct);
        return rows.Select(row=>new StructuredFormOfferReference(row.RequestId,row.DefinitionId,row.VersionId,
            JsonSerializer.Deserialize<Dictionary<string,string>>(row.NameJson,JsonSerializerOptions.Web)![row.Culture],row.Declined)).ToArray();
    }
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
            outgoing.FlowSession==null?null:new WhatsAppFormPresentationRow(outgoing.FlowSession.DefinitionId,outgoing.FlowSession.VersionId,outgoing.FlowSession.VersionNumber,outgoing.FlowSession.NameJson,outgoing.FlowSession.Culture),outgoing.Origin)).SingleOrDefaultAsync(ct);
}
