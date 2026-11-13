using ArquitecturaBase.Application.Interfaces.Integrations.Phones;
using ArquitecturaBase.Application.Models.StructuredData;
using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Readers;
internal sealed class WhatsAppRecordOriginReader(ApplicationDbContext dbContext, IPhoneNumberParser phones) : IWhatsAppRecordOriginReader
{
    public async Task<IReadOnlyList<StructuredRecordOrigin>> ListAsync(IReadOnlyCollection<Guid> sourceReceiptIds,bool canOpenConversation,CancellationToken cancellationToken)
    {
        var rows=await Rows(sourceReceiptIds).ToListAsync(cancellationToken);
        var resent=await ResentAsync([.. rows.Select(row=>row.Id)],cancellationToken);
        return rows.Select(row=>{
            var phone=phones.Parse(null,row.WaId is null?null:"+"+row.WaId);
            var shared=Shared(row.SharedContentJson);
            return new StructuredRecordOrigin(row.Id,"phone",row.OperatorUserId,row.Sender,row.RequestedAtUtc,
                canOpenConversation?row.ContactId:null,phone.IsSuccess?phones.Mask(phone.Value):null,row.SenderKind)
            {
                // W1-A: the location asked before the Flow, when the send asked for it.
                LocationRequestedAtUtc=row.LocationState==WhatsAppFlowLocationState.None?null:row.CreatedAtUtc,
                LocationResolvedAtUtc=row.LocationResolvedAtUtc, LocationSkipped=row.LocationState==WhatsAppFlowLocationState.Skipped,
                // W3 (task 24): what the send took while it was open, without values, and the resends that went out.
                SharedLocation=shared.Location is {} location?new(location.AtUtc,location.FieldIds):null,
                SharedContact=shared.Contact is {} contact?new(contact.AtUtc,contact.FieldIds):null,
                ResentAtUtc=resent.GetValueOrDefault(row.Id)??[],
            };
        }).ToArray();
    }
    // A send of W1-A has more outgoing messages than its Flow: the location request and, if optional, its skip button. A resend of W3
    // is another Flow of the same send: the origin is its first Flow, the only one of each send that is not a resend.
    private IQueryable<OriginRow> Rows(IReadOnlyCollection<Guid> sourceReceiptIds)=>
        from session in dbContext.Set<WhatsAppFlowSession>().AsNoTracking()
        where sourceReceiptIds.Contains(session.Id)
        join outgoing in dbContext.Set<WhatsAppOutgoingMessage>().Where(item=>item.Kind==WhatsAppOutgoingMessageKind.Flow && !item.IsResend) on (Guid?)session.Id equals outgoing.FlowSessionId
        join contact in dbContext.Set<WhatsAppContact>() on session.ContactId equals contact.Id
        select new OriginRow(session.Id,session.OperatorUserId,contact.WaId,session.ContactId,outgoing.RequestedAtUtc,
            dbContext.Users.Where(user=>user.Id==session.OperatorUserId).Select(user=>user.DisplayName).FirstOrDefault(),session.Origin==WhatsAppMessageOrigin.Assistant?"Assistant":"Manual",
            session.LocationState,session.CreatedAtUtc,session.LocationResolvedAtUtc,session.SharedContentJson);
    // W3 (task 24): the resends that went out (Meta accepted them or a person reviewed them as sent), oldest first, by when they were asked
    // for, as the first Flow.
    private async Task<IReadOnlyDictionary<Guid,IReadOnlyList<DateTime>>> ResentAsync(Guid[] sessionIds,CancellationToken cancellationToken)
    {
        if(sessionIds.Length==0)return new Dictionary<Guid,IReadOnlyList<DateTime>>();
        var resends=await dbContext.Set<WhatsAppOutgoingMessage>().AsNoTracking()
            .Where(item=>item.FlowSessionId!=null && sessionIds.Contains(item.FlowSessionId.Value) && item.Kind==WhatsAppOutgoingMessageKind.Flow && item.IsResend
                && (item.State==WhatsAppOutgoingMessageState.Accepted || item.State==WhatsAppOutgoingMessageState.Reviewed))
            .Select(item=>new{SessionId=item.FlowSessionId!.Value,item.RequestedAtUtc}).ToListAsync(cancellationToken);
        return resends.GroupBy(item=>item.SessionId).ToDictionary(group=>group.Key,group=>(IReadOnlyList<DateTime>)[.. group.Select(item=>item.RequestedAtUtc).Order()]);
    }
    // What the send took is only for the history of the detail: one that does not read is left out, and the page or export still reads.
    private static WhatsAppFlowSharedContent Shared(string? json)
    {
        try { return WhatsAppFlowSession.ReadSharedContent(json); }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException) { return new(null, null); }
    }
    private sealed record OriginRow(Guid Id,Guid? OperatorUserId,string? WaId,Guid ContactId,DateTime RequestedAtUtc,string? Sender,string SenderKind,
        WhatsAppFlowLocationState LocationState,DateTime CreatedAtUtc,DateTime? LocationResolvedAtUtc,string? SharedContentJson);
}
