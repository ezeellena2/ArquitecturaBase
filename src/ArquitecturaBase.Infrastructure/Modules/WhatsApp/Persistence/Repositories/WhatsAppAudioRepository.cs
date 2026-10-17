using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;
internal sealed class WhatsAppAudioRepository(ApplicationDbContext db):IWhatsAppAudioRepository
{
    public async Task<WhatsAppMessage?> GetForUpdateAsync(Guid contactId,Guid messageId,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppMessage>().FromSql($"""SELECT * FROM "WhatsAppMessages" WHERE "ContactId"={contactId} AND "Id"={messageId} AND "Kind"='Audio' AND "Direction"='Inbound' FOR NO KEY UPDATE""").ToListAsync(ct);
        return rows.SingleOrDefault();
    }
    public async Task<WhatsAppMessage?> GetByRequestForUpdateAsync(Guid contactId,Guid requestId,CancellationToken ct)
    {
        db.RequireTransaction();
        var rows=await db.Set<WhatsAppMessage>().FromSql($"""SELECT * FROM "WhatsAppMessages" WHERE "ContactId"={contactId} AND "AudioRequestId"={requestId} FOR NO KEY UPDATE""").ToListAsync(ct);
        return rows.SingleOrDefault();
    }
}
