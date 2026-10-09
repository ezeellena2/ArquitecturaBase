using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using ArquitecturaBase.Infrastructure.Persistence.Extensions;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

internal sealed class WhatsAppConversationRepository(ApplicationDbContext dbContext) : IWhatsAppConversationRepository
{
    public async Task<WhatsAppContact?> GetForUpdateAsync(Guid contactId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var rows = await dbContext.Set<WhatsAppContact>()
            .FromSql($"""SELECT * FROM "WhatsAppContacts" WHERE "Id" = {contactId} FOR NO KEY UPDATE""")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<WhatsAppReadState?> GetReadStateAsync(Guid contactId, Guid operatorUserId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppReadState>().SingleOrDefaultAsync(item => item.ContactId == contactId && item.OperatorUserId == operatorUserId, cancellationToken);

    public Task<WhatsAppOutgoingMessage?> GetByRequestAsync(Guid contactId, Guid operatorUserId, Guid requestId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppOutgoingMessage>().SingleOrDefaultAsync(item =>
            item.ContactId == contactId && item.OperatorUserId == operatorUserId && item.RequestId == requestId, cancellationToken);

    public async Task<WhatsAppOutgoingMessage?> GetOutgoingAsync(Guid outgoingId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var rows = await dbContext.Set<WhatsAppOutgoingMessage>()
            .FromSql($"""SELECT * FROM "WhatsAppOutgoingMessages" WHERE "Id" = {outgoingId} FOR NO KEY UPDATE""")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public Task<bool> ExistsUnresolvedOutgoingAsync(Guid contactId, CancellationToken cancellationToken) =>
        dbContext.Set<WhatsAppOutgoingMessage>().AnyAsync(item => item.ContactId == contactId
            && (item.State == WhatsAppOutgoingMessageState.Pending || item.State == WhatsAppOutgoingMessageState.Sending
                || item.State == WhatsAppOutgoingMessageState.Uncertain), cancellationToken);

    public async Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var rows = await dbContext.Set<WhatsAppOutgoingMessage>()
            .FromSql($"""SELECT * FROM "WhatsAppOutgoingMessages" WHERE "State" = 'Pending' ORDER BY "RequestedAtUtc", "Id" LIMIT 1 FOR NO KEY UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<WhatsAppOutgoingMessage>> ListExpiredSendingAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        return await dbContext.Set<WhatsAppOutgoingMessage>()
            .FromSql($"""SELECT * FROM "WhatsAppOutgoingMessages" WHERE "State" = 'Sending' AND "StartedAtUtc" < {cutoffUtc} ORDER BY "Id" FOR NO KEY UPDATE SKIP LOCKED""")
            .ToListAsync(cancellationToken);
    }

    public void Add(WhatsAppReadState state) => dbContext.Set<WhatsAppReadState>().Add(state);
    public void Add(WhatsAppOutgoingMessage outgoing) => dbContext.Set<WhatsAppOutgoingMessage>().Add(outgoing);
}
