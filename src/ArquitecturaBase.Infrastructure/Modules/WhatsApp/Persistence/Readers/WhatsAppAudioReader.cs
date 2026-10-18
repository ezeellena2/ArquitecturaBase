using System.Linq.Expressions;
using ArquitecturaBase.Application.Modules.WhatsApp.Configuration;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal sealed class WhatsAppAudioReader(ApplicationDbContext db,WhatsAppContentRetention retention,TimeProvider clock):IWhatsAppAudioReader
{
    private IQueryable<WhatsAppMessage> Audio()=>db.Set<WhatsAppMessage>().AsNoTracking().Where(message=>message.Kind==WhatsAppMessageKind.Audio && message.Direction==WhatsAppMessageDirection.Inbound && message.ContactId!=null && message.AudioState!=null);
    private IQueryable<WhatsAppAudioSourceRow> Rows()=>Project(Audio());
    private IQueryable<WhatsAppAudioSourceRow> Project(IQueryable<WhatsAppMessage> audio)=>from message in audio
        join contact in db.Set<WhatsAppContact>().AsNoTracking() on message.ContactId equals contact.Id
        select new WhatsAppAudioSourceRow
        {
            MessageId=message.Id,ContactId=contact.Id,OccurredAtUtc=message.OccurredAtUtc,Mode=contact.Mode,LinkedUserId=contact.UserId,LinkRevision=contact.LinkRevision,State=message.AudioState!.Value,
            MediaId=message.AudioMediaId,ContentType=message.AudioContentType,Sha256=message.AudioSha256,StorageKey=message.AudioStorageKey,Size=message.AudioSize,RequestId=message.AudioRequestId,ActorUserId=message.AudioActorUserId,
            ContextJson=message.AudioContextJson,CapturedLinkRevision=message.AudioLinkRevision,ContentExpiresAtUtc=message.AudioContentExpiresAtUtc,
        };
    private IQueryable<WhatsAppAudioSourceRow> Pending(DateTime nowUtc)
    {
        var cutoff=nowUtc.AddDays(-retention.MessageRetentionDays);
        return Project(Audio().Where(RetryDueAt(nowUtc))).Where(row=>(row.State==WhatsAppAudioState.Pending || row.State==WhatsAppAudioState.Transcribing) && row.OccurredAtUtc>cutoff
            && (row.ContentExpiresAtUtc==null || row.ContentExpiresAtUtc>nowUtc) && (row.StorageKey==null || row.RequestId!=null || row.Mode==WhatsAppConversationMode.Assistant && row.LinkedUserId!=null));
    }
    internal static Expression<Func<WhatsAppMessage,bool>> RetryDueAt(DateTime nowUtc)=>message=>
        message.AudioFirstFailureAtUtc==null || message.AudioFirstFailureAtUtc.Value.AddSeconds(
            message.AudioFailureCount<=1?30:message.AudioFailureCount==2?90:message.AudioFailureCount==3?210:message.AudioFailureCount==4?450:930)<=nowUtc;
    public async Task<IReadOnlyList<Guid>> ListPendingContactsAsync(IReadOnlyCollection<Guid> excluded,int limit,DateTime nowUtc,CancellationToken ct)=>
        await Pending(nowUtc).Where(row=>!excluded.Contains(row.ContactId)).Select(row=>row.ContactId).Distinct().OrderBy(id=>id).Take(limit).ToListAsync(ct);
    public Task<WhatsAppAudioSourceRow?> FindPendingAsync(Guid contactId,DateTime nowUtc,CancellationToken ct)=>Pending(nowUtc).Where(row=>row.ContactId==contactId)
        .OrderBy(row=>row.OccurredAtUtc).ThenBy(row=>row.MessageId).FirstOrDefaultAsync(ct);
    public Task<WhatsAppAudioSourceRow?> FindAsync(Guid contactId,Guid messageId,CancellationToken ct)=>Rows().SingleOrDefaultAsync(row=>row.ContactId==contactId && row.MessageId==messageId,ct);
    public async Task<IReadOnlyList<string>> ListReferencedKeysAsync(IReadOnlyList<string> storageKeys,DateTime cutoffUtc,CancellationToken ct)
    {
        var ids=storageKeys.Where(key=>key.StartsWith("whatsapp/audio/",StringComparison.Ordinal) && key.EndsWith(".ogg",StringComparison.Ordinal))
            .Select(key=>Guid.TryParseExact(key[15..^4],"N",out var id)?id:Guid.Empty).Where(id=>id!=Guid.Empty).ToArray();
        var now=clock.GetUtcNow().UtcDateTime;
        var rows=await Audio().Where(row=>row.OccurredAtUtc>cutoffUtc && (row.AudioContentExpiresAtUtc==null || row.AudioContentExpiresAtUtc>now)
            && (ids.Contains(row.Id) || row.AudioStorageKey!=null && storageKeys.Contains(row.AudioStorageKey)))
            .Select(row=>new{row.Id,row.AudioStorageKey}).ToListAsync(ct);
        var requested=storageKeys.ToHashSet(StringComparer.Ordinal);
        return rows.SelectMany(row=>new[]{row.AudioStorageKey,$"whatsapp/audio/{row.Id:N}.ogg"}).Where(key=>key is not null && requested.Contains(key)).Select(key=>key!).Distinct(StringComparer.Ordinal).ToArray();
    }
}
