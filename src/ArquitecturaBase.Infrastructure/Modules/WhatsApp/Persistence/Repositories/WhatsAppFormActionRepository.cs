using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;
namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppFormActionRepository(ApplicationDbContext dbContext) : IWhatsAppFormActionRepository
{
    public async Task LockAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken)
    {
        await dbContext.AcquireAdvisoryLocksAsync([WhatsAppLockKeys.FormAction(runId, recipientUserId)], cancellationToken);
        // Status may have been read in this scope before another host completed the send.
        foreach (var tracked in dbContext.ChangeTracker.Entries<WhatsAppOutgoingMessage>().Where(entry =>
            entry.Entity.Origin == WhatsAppMessageOrigin.FormAction && entry.Entity.RequestId == runId
            && entry.Entity.RecipientUserId == recipientUserId).ToArray())
            await tracked.ReloadAsync(cancellationToken);
    }
    public Task<WhatsAppOutgoingMessage?> GetByRunAsync(Guid runId, Guid recipientUserId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppOutgoingMessage>().SingleOrDefaultAsync(item => item.Origin == WhatsAppMessageOrigin.FormAction
            && item.RequestId == runId && item.RecipientUserId == recipientUserId, cancellationToken);
    public async Task<WhatsAppContact?> GetContactForUpdateAsync(Guid recipientUserId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var rows = await dbContext.Set<WhatsAppContact>()
            .FromSql($"""SELECT * FROM "WhatsAppContacts" WHERE "UserId" = {recipientUserId} FOR NO KEY UPDATE""")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
    public void Add(WhatsAppOutgoingMessage outgoing) => dbContext.Set<WhatsAppOutgoingMessage>().Add(outgoing);
}
