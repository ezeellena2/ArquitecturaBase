using ArquitecturaBase.Application.Modules.WhatsApp.Interfaces.Persistence;
using ArquitecturaBase.Domain.Modules.WhatsApp;
using ArquitecturaBase.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ArquitecturaBase.Infrastructure.Modules.WhatsApp.Persistence.Repositories;

/// <summary>
/// Vacía el texto de los mensajes vencidos. Queda fuera de IUnitOfWork.ExecuteInTransactionAsync a propósito: es una
/// sola sentencia, idempotente, en autocommit, sobre una entidad que no es IAuditable ni ISoftDeletable. Es la única
/// clase que puede usar ExecuteUpdate (lo verifica TransactionBoundaryTests). WhatsAppMessageRetentionService la corre
/// aunque WhatsApp esté apagado.
/// </summary>
internal sealed class WhatsAppMessageRetentionRepository(ApplicationDbContext dbContext)
    : IWhatsAppMessageRetentionRepository
{
    public async Task<int> ClearExpiredBodiesAsync(DateTime cutoffUtc, CancellationToken cancellationToken)
    {
        // ExecuteUpdate no pasa por los interceptores. WhatsAppMessage no es IAuditable ni ISoftDeletable.
        // Sin índice por fecha: una corrida diaria recorre la tabla entera a esta escala. Si crece, un índice parcial
        // sobre OccurredAtUtc con "Body" IS NOT NULL deja afuera los mensajes que ya se vaciaron.
        var messages = await dbContext.Set<WhatsAppMessage>()
            .Where(message => message.Body != null && message.OccurredAtUtc < cutoffUtc)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Body, (string?)null), cancellationToken);
        var outgoing = await dbContext.Set<WhatsAppOutgoingMessage>()
            .Where(message => message.Body != null && message.RequestedAtUtc < cutoffUtc
                && message.State != WhatsAppOutgoingMessageState.Pending && message.State != WhatsAppOutgoingMessageState.Sending)
            .ExecuteUpdateAsync(setters => setters.SetProperty(message => message.Body, (string?)null), cancellationToken);
        return messages + outgoing;
    }
}
