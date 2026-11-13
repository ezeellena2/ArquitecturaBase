using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppAssistantRepository(ApplicationDbContext db) : IWhatsAppAssistantRepository
{
    public async Task<WhatsAppAssistantRequest?> GetByRequestAsync(Guid requestId,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppAssistantRequest>().FromSql($"""SELECT * FROM "WhatsAppAssistantRequests" WHERE "RequestId"={requestId} FOR NO KEY UPDATE""").ToListAsync(ct);
        var row=rows.SingleOrDefault();
        if(row is not null) await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    public async Task<WhatsAppAssistantRequest?> GetBySourceAsync(Guid contactId,Guid sourceMessageId,WhatsAppAssistantRequestKind kind,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppAssistantRequest>().FromSql($"""SELECT * FROM "WhatsAppAssistantRequests" WHERE "ContactId"={contactId} AND "SourceMessageId"={sourceMessageId} AND "Kind"={kind.ToString()} FOR NO KEY UPDATE""").ToListAsync(ct);
        var row=rows.SingleOrDefault();
        if(row is not null) await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    public async Task<WhatsAppAssistantRequest?> GetByCapturedRecordAsync(Guid contactId,Guid recordId,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppAssistantRequest>().FromSql($"""SELECT * FROM "WhatsAppAssistantRequests" WHERE "ContactId"={contactId} AND "SourceRecordId"={recordId} FOR NO KEY UPDATE""").ToListAsync(ct);
        var row=rows.SingleOrDefault();
        if(row is not null) await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    public async Task<WhatsAppAssistantRequest?> GetPendingAsync(Guid contactId,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppAssistantRequest>().FromSql($"""SELECT * FROM "WhatsAppAssistantRequests" WHERE "ContactId"={contactId} AND "State"='Pending' ORDER BY "CreatedAtUtc","Id" LIMIT 1 FOR NO KEY UPDATE""").ToListAsync(ct);
        var row=rows.SingleOrDefault();
        if(row is not null) await db.Entry(row).ReloadAsync(ct);
        return row;
    }
    // Task 9c: only read, to copy it into the reference of the reply, so it takes no lock.
    public Task<WhatsAppAssistantRequest?> GetBySessionAsync(Guid contactId,Guid sessionId,CancellationToken ct)
    {
        db.RequireTransaction();
        return db.Set<WhatsAppAssistantRequest>().Where(row=>row.ContactId==contactId && row.OutgoingId!=null
                && db.Set<WhatsAppOutgoingMessage>().Any(outgoing=>outgoing.Id==row.OutgoingId && outgoing.FlowSessionId==sessionId))
            .OrderBy(row=>row.CreatedAtUtc).ThenBy(row=>row.Id).FirstOrDefaultAsync(ct);
    }
    public void Add(WhatsAppAssistantRequest request)=>db.Set<WhatsAppAssistantRequest>().Add(request);
}
