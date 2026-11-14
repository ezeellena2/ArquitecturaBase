using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Application.Modules.WhatsApp.Models;
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
        dbContext.Set<WhatsAppOutgoingMessage>().AnyAsync(item => item.ContactId == contactId && item.Origin == WhatsAppMessageOrigin.Conversation
            && (item.State == WhatsAppOutgoingMessageState.Pending || item.State == WhatsAppOutgoingMessageState.Sending
                || item.State == WhatsAppOutgoingMessageState.Uncertain), cancellationToken);

    public async Task<IReadOnlyList<WhatsAppAssistantRequest>> ListPendingAutomaticRequestsAsync(Guid contactId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        return await dbContext.Set<WhatsAppAssistantRequest>().Where(item => item.ContactId == contactId
            && item.Kind == WhatsAppAssistantRequestKind.Automatic && item.State == WhatsAppAssistantRequestState.Pending)
            .OrderBy(item => item.CreatedAtUtc).ThenBy(item => item.Id).ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsStartedAssistantMessageAsync(Guid contactId, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        return dbContext.Set<WhatsAppOutgoingMessage>().AnyAsync(item => item.ContactId == contactId
            && item.Origin == WhatsAppMessageOrigin.Assistant && item.State == WhatsAppOutgoingMessageState.Sending
            && item.MessageEffectStartedAtUtc.HasValue, cancellationToken);
    }

    public async Task<WhatsAppOutgoingMessage?> GetNextPendingAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        dbContext.RequireTransaction();
        var waitingSinceUtc = nowUtc - WhatsAppDispatchPacing.RecipientWait;
        // Preparation can be recovered to Pending. Always take the contact before its outgoing row,
        // matching Mark/Complete even if a stale worker is still waiting for the destination lease.
        // H12: a contact with a send in progress is not taken, so a second copy cannot overtake it (H23); one in its wait goes after
        // the others and, alone, waits its turn at the lease. H31: what answers someone goes before a rule's notice. H7: a chat whose
        // head waits for its next attempt does not go out, so nothing overtakes it.
        var contact = (await dbContext.Set<WhatsAppContact>()
            .FromSql($"""
                SELECT c.* FROM "WhatsAppContacts" AS c
                JOIN "WhatsAppOutgoingMessages" AS o ON o."ContactId" = c."Id"
                WHERE o."State" = 'Pending' AND (c."LastDispatchAtUtc" IS NULL OR c."LastDispatchAtUtc" <= {waitingSinceUtc})
                  AND NOT EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" AS s WHERE s."ContactId" = c."Id"
                    AND (s."State" = 'Sending' OR s."State" = 'Pending' AND s."NextAttemptAtUtc" > {nowUtc}))
                ORDER BY o."IsNotice", o."RequestedAtUtc", o."Id"
                LIMIT 1 FOR NO KEY UPDATE OF c SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)).SingleOrDefault()
            ?? (await dbContext.Set<WhatsAppContact>()
            .FromSql($"""
                SELECT c.* FROM "WhatsAppContacts" AS c
                JOIN "WhatsAppOutgoingMessages" AS o ON o."ContactId" = c."Id"
                WHERE o."State" = 'Pending' AND c."LastDispatchAtUtc" > {waitingSinceUtc}
                  AND NOT EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" AS s WHERE s."ContactId" = c."Id"
                    AND (s."State" = 'Sending' OR s."State" = 'Pending' AND s."NextAttemptAtUtc" > {nowUtc}))
                ORDER BY o."IsNotice", o."RequestedAtUtc", o."Id"
                LIMIT 1 FOR NO KEY UPDATE OF c SKIP LOCKED
                """)
            .ToListAsync(cancellationToken)).SingleOrDefault();
        if (contact is null) return null;
        // Within a chat the order stays, whatever the mark. The send in progress is asked again here, with the contact already
        // locked and a fresh snapshot: the choice above may have seen the contact before another copy's reservation committed.
        var rows = await dbContext.Set<WhatsAppOutgoingMessage>()
            .FromSql($"""
                SELECT * FROM "WhatsAppOutgoingMessages" WHERE "ContactId" = {contact.Id} AND "State" = 'Pending'
                  AND NOT EXISTS (SELECT 1 FROM "WhatsAppOutgoingMessages" AS s WHERE s."ContactId" = {contact.Id}
                    AND (s."State" = 'Sending' OR s."State" = 'Pending' AND s."NextAttemptAtUtc" > {nowUtc}))
                ORDER BY "RequestedAtUtc", "Id" LIMIT 1 FOR NO KEY UPDATE SKIP LOCKED
                """)
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
